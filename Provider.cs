using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Web;
using System.Xml;
using DotNetNuke.Entities.Portals;
using NBrightCore.common;
using NBrightDNN;
using Nevoweb.DNN.NBrightBuy.Components;
using Nevoweb.DNN.NBrightBuy.Components.Interfaces;

namespace Nevoweb.DNN.NBrightBuyChronopost
{
    public class Provider : ShippingInterface 
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="cartInfo"></param>
        /// <returns></returns>
        public override NBrightInfo CalculateShipping(NBrightInfo cartInfo)
        {
            var modCtrl = new NBrightBuyController();
            var info = modCtrl.GetByGuidKey(PortalSettings.Current.PortalId, -1, "SHIPPING", Shippingkey);
            if (info == null) return cartInfo;

            var defData = GetDefaultvalues(info, cartInfo);

            if (defData.regioncode == "" || (defData.freeshippinglimit > 0 && (defData.freeshippinglimit <= defData.totalcost) || defData.totalweight == 0))
            {
                // return zero if we have invalid data
                cartInfo.SetXmlPropertyDouble("genxml/shippingcost", "0");
                cartInfo.SetXmlPropertyDouble("genxml/shippingcostTVA", "0");
                cartInfo.SetXmlPropertyDouble("genxml/shippingdealercost", "0");
                cartInfo.SetXmlProperty("genxml/chronopostmessage", "");
                return cartInfo;  
            }

            // get soap xml from resx
            var soapxmlfilename = HttpContext.Current.Server.MapPath("/DesktopModules/NBright/NBrightBuyChronopost/soapquickcost.xml");
            var xmlDoc = new XmlDocument();
            xmlDoc.Load(soapxmlfilename);
            var soapxml = xmlDoc.OuterXml;
            // replace the tokens in the soap XML strucutre.
            soapxml = soapxml.Replace("{accountnumber}", defData.accountnumber);
            soapxml = soapxml.Replace("{password}", defData.password);
            soapxml = soapxml.Replace("{depcode}", defData.departurecode);
            soapxml = soapxml.Replace("{arrcode}", defData.arrivalcode);
            soapxml = soapxml.Replace("{weight}", defData.totalweight.ToString("F"));
            soapxml = soapxml.Replace("{productcode}", defData.productcode);
            soapxml = soapxml.Replace("{type}", defData.chronoposttype);

            var nbi = GetSoapReturn(soapxml, "https://www.chronopost.fr/quickcost-cxf/QuickcostServiceWS");

            XmlDocument doc = new XmlDocument();
            doc.LoadXml(nbi.XMLData);
            var nsMgr = new XmlNamespaceManager(doc.NameTable);
            nsMgr.AddNamespace("soap", "http://schemas.xmlsoap.org/soap/envelope/");
            nsMgr.AddNamespace("ns1", "http://cxf.quickcost.soap.chronopost.fr/");
            Double shippingcostTCC = 0;
            Double shippingcostTVA = 0;
            Double shippingcost = 0;
            var shippingmsg = "";

            var shippingnod = doc.SelectSingleNode("/soap:Envelope/soap:Body/ns1:quickCostResponse/return/amountTTC", nsMgr);
            if (shippingnod != null && Utils.IsNumeric(shippingnod.InnerText)) shippingcost = Convert.ToDouble(shippingnod.InnerText);
            shippingnod = doc.SelectSingleNode("/soap:Envelope/soap:Body/ns1:quickCostResponse/return/amountTVA", nsMgr);
            if (shippingnod != null && Utils.IsNumeric(shippingnod.InnerText)) shippingcostTVA = Convert.ToDouble(shippingnod.InnerText);
            shippingnod = doc.SelectSingleNode("/soap:Envelope/soap:Body/ns1:quickCostResponse/return/errorMessage", nsMgr);
            if (shippingnod != null) shippingmsg = shippingnod.InnerText;

            var shippingdealercost = shippingcost;
            cartInfo.SetXmlPropertyDouble("genxml/shippingcost", shippingcost);
            cartInfo.SetXmlPropertyDouble("genxml/shippingcostTVA", shippingcostTVA);
            cartInfo.SetXmlPropertyDouble("genxml/shippingdealercost", shippingdealercost);
            cartInfo.SetXmlProperty("genxml/chronopostmessage", shippingmsg);

            return cartInfo;
        }

        public override string Shippingkey { get; set; }

        public override string Name()
        {
            return "Standard";
        }

        public override string GetTemplate(NBrightInfo cartInfo)
        {
            return GetTemplateData("carttemplate.html", cartInfo); ;
        }

        public override string GetDeliveryLabelUrl(NBrightInfo cartInfo)
        {
            var rtnUrl = "https://www.chronopost.fr/shipping-cxf/getReservedSkybill?reservationNumber=";

            var modCtrl = new NBrightBuyController();
            var info = modCtrl.GetByGuidKey(PortalSettings.Current.PortalId, -1, "SHIPPING", Shippingkey);
            if (info == null) return "";

            var defData = GetDefaultvalues(info, cartInfo);

            // get soap xml from resx
            var soapxmlfilename = HttpContext.Current.Server.MapPath("/DesktopModules/NBright/NBrightBuyChronopost/soaplabel.xml");
            var xmlDoc = new XmlDocument();
            xmlDoc.Load(soapxmlfilename);
            var soapxml = xmlDoc.OuterXml;


            // replace the tokens in the soap XML strucutre.
            soapxml = soapxml.Replace("{accountNumber}", defData.accountnumber);
            soapxml = soapxml.Replace("{password}", defData.password);
            soapxml = soapxml.Replace("{weight}", defData.totalweight.ToString("F"));
            soapxml = soapxml.Replace("{productcode}", defData.productcode);

            soapxml = soapxml.Replace("{shipperCountry}", defData.distributioncountrycode);
            soapxml = soapxml.Replace("{shipperCountryName}", defData.distributioncountryname);

            foreach (var s in StoreSettings.Current.Settings())
            {
                soapxml = soapxml.Replace("{" + s.Key + "}", s.Value);                
            }

            foreach (var s in cartInfo.ToDictionary("genxml/shipaddress/"))
            {
                soapxml = soapxml.Replace("{" + s.Key + "}", s.Value);                
            }

            soapxml = soapxml.Replace("{recipientPreAlert}", "0");
            soapxml = soapxml.Replace("{ordernumber}", cartInfo.GetXmlProperty("genxml/ordernumber"));
            soapxml = soapxml.Replace("{productcode}", cartInfo.GetXmlProperty("genxml/extrainfo/genxml/radiobuttonlist/chronopostproductcode"));
            DateTime shippingdate = DateTime.Today;
            if (Utils.IsDate(cartInfo.GetXmlProperty("genxml/textbox/shippingdate"))) shippingdate = Convert.ToDateTime(cartInfo.GetXmlProperty("genxml/textbox/shippingdate"));
            soapxml = soapxml.Replace("{shipdate}", shippingdate.ToString("yyyy-MM-dd") + "Y12:00:00.000Z");

            soapxml = soapxml.Replace("{mode}", defData.printmode);

            if (defData.chronoposttype == "D")
                soapxml = soapxml.Replace("{objecttype}", "DOC");
            else
                soapxml = soapxml.Replace("{objecttype}", "MAR");

            if (defData.productcode == "86")
                soapxml = soapxml.Replace("{recipientref}", cartInfo.GetXmlProperty("genxml/extrainfo/genxml/radiobuttonlist/chronopostrelais"));
            else
                soapxml = soapxml.Replace("{recipientref}", cartInfo.GetXmlProperty("genxml/textbox/trackingcode"));
            
            // string any unmatch tokens
            var aryTokens = Utils.ParseTemplateText(soapxml,"{","}");
            var lp = 1;
            soapxml = "";
            foreach (var s in aryTokens)
            {
                if (lp % 2 != 0) soapxml += s;
                lp += 1;
            }

            var nbi = GetSoapReturn(soapxml, "https://www.chronopost.fr/shipping-cxf/ShippingServiceWS");

            XmlDocument doc = new XmlDocument();
            doc.LoadXml(nbi.XMLData);
            var nsMgr = new XmlNamespaceManager(doc.NameTable);
            nsMgr.AddNamespace("soap", "http://schemas.xmlsoap.org/soap/envelope/");
            nsMgr.AddNamespace("ns1", "http://cxf.shipping.soap.chronopost.fr/");
            
            var resvnumber = "";
            var shippingnod = doc.SelectSingleNode("/soap:Envelope/soap:Body/ns1:shippingWithReservationAndESDWithRefClientResponse/return/reservationNumber", nsMgr);
            if (shippingnod != null && Utils.IsNumeric(shippingnod.InnerText)) resvnumber = shippingnod.InnerText;

            if (resvnumber == "")
            {
                doc.Save(PortalSettings.Current.HomeDirectoryMapPath + "\\debug_chronoposterr.xml");
                return "";
            }

            rtnUrl += resvnumber;

            return rtnUrl;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="soapxml"></param>
        /// <returns></returns>
        private NBrightInfo GetSoapReturn(String soapxml,String url)
        {
            if (StoreSettings.Current.DebugMode)
            {
                var nbi = new NBrightInfo();
                nbi.XMLData = soapxml;
                nbi.XMLDoc.Save(PortalSettings.Current.HomeDirectoryMapPath + "\\debug_chronopostsoap.xml");
            }

            using (var client = new WebClient())
            {
                // the Content-Type needs to be set to XML
                client.Headers.Add("Content-Type", "text/xml;charset=utf-8");
                // The SOAPAction header indicates which method you would like to invoke
                // and could be seen in the WSDL: <soap:operation soapAction="..." /> element
                client.Headers.Add("SOAPAction", "");
                var response = client.UploadString(url, soapxml);
                var nbi = new NBrightInfo();
                nbi.XMLData = response;
                return nbi;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="templatename"></param>
        /// <param name="cartInfo"></param>
        /// <returns></returns>
        private String GetTemplateData(String templatename,NBrightInfo cartInfo)
        {
            var modCtrl = new NBrightBuyController();
            var info = modCtrl.GetByGuidKey(PortalSettings.Current.PortalId, -1, "SHIPPING", Shippingkey);
            if (info == null) return "";

            var defData = GetDefaultvalues(info, cartInfo);

            if (!StoreSettings.Current.Settings().ContainsKey("chronopostkey")) StoreSettings.Current.Settings().Add("chronopostkey", Shippingkey);


            var controlMapPath = HttpContext.Current.Server.MapPath("/DesktopModules/NBright/NBrightBuyChronopost");
            var templCtrl = new NBrightCore.TemplateEngine.TemplateGetter(PortalSettings.Current.HomeDirectoryMapPath, controlMapPath, "Themes\\config", "");
            var templ = templCtrl.GetTemplateData(templatename, Utils.GetCurrentCulture());

            // replace dropdownlist with valid values
            templ = templ.Replace("{chronopostproductcodedata}", defData.data);
            templ = templ.Replace("{chronopostproductcodedatavalue}", defData.datavalue);

            //if we have the relais server display the points
            if (defData.productcode == "86") templ += GetRelaisTemplate(defData); 

            templ = Utils.ReplaceSettingTokens(templ, StoreSettings.Current.Settings());
            templ = Utils.ReplaceUrlTokens(templ);
            return templ;
        }

        private String GetRelaisTemplate(ChronopostDefaults defData)
        {

            // get soap xml from resx
            var soapxmlfilename = HttpContext.Current.Server.MapPath("/DesktopModules/NBright/NBrightBuyChronopost/soappointlist.xml");
            var xmlDoc = new XmlDocument();
            xmlDoc.Load(soapxmlfilename);
            var soapxml = xmlDoc.OuterXml;
            // replace the tokens in the soap XML strucutre.
            soapxml = soapxml.Replace("{codeProduit}", defData.productcode);
            soapxml = soapxml.Replace("{codePostal}", defData.postalcode);
            var daysadd = 5;
            if (Utils.IsNumeric(defData.leaddays)) daysadd = Convert.ToInt32(defData.leaddays);
            var pickupdate = DateTime.Now.AddDays(daysadd);
            if (pickupdate.DayOfWeek == DayOfWeek.Saturday || pickupdate.DayOfWeek == DayOfWeek.Sunday) pickupdate = pickupdate.AddDays(2);
            soapxml = soapxml.Replace("{date}", pickupdate.ToString("dd/MM/yyyy"));

            var nbi = GetSoapReturn(soapxml, "https://www.chronopost.fr/recherchebt-ws-cxf/PointRelaisServiceWS");

            XmlDocument doc = new XmlDocument();
            doc.LoadXml(nbi.XMLData);
            var nsMgr = new XmlNamespaceManager(doc.NameTable);
            nsMgr.AddNamespace("soap", "http://schemas.xmlsoap.org/soap/envelope/");
            nsMgr.AddNamespace("ns1", "http://cxf.rechercheBt.soap.chronopost.fr/");

            if (StoreSettings.Current.DebugMode) doc.Save(PortalSettings.Current.HomeDirectoryMapPath + "\\debug_chronopostquickcostrtn.xml");

            // build list of points
            var relaisDicList = new List<NBrightInfo>();
            var nodList = doc.SelectNodes("/soap:Envelope/soap:Body/ns1:rechercheBtParCodeproduitEtCodepostalEtDateResponse/*", nsMgr);
            if (nodList != null)
            {
                foreach (XmlNode nod in nodList)
                {
                    var nbirelais = new NBrightInfo(true);
                    nbirelais.XMLData = nod.OuterXml.ToLower();
                    nbirelais.GUIDKey = nbirelais.GetXmlProperty("return/identifiantChronopostPointA2PAS");
                    relaisDicList.Add(nbirelais);
                }
            }

            var relaisData = "";
            var relaisDataValue = "";
            var templName = "relaisbody.html";
            if (!relaisDicList.Any()) templName = "relaisfail.html";


            // get templates
            var controlMapPath = HttpContext.Current.Server.MapPath("/DesktopModules/NBright/NBrightBuyChronopost");
            var templCtrl = new NBrightCore.TemplateEngine.TemplateGetter(PortalSettings.Current.HomeDirectoryMapPath, controlMapPath, "Themes\\config", "");
            var rtnTemplh = templCtrl.GetTemplateData("relaisheader.html", Utils.GetCurrentCulture());
            var rtnTemplf = templCtrl.GetTemplateData("relaisfooter.html", Utils.GetCurrentCulture());
            var rtnTemplb = templCtrl.GetTemplateData(templName, Utils.GetCurrentCulture());

            var rtnTempl = rtnTemplh;
            if (relaisDicList.Any())
                rtnTempl += NBrightCore.render.GenXmlFunctions.RenderRepeater(relaisDicList, rtnTemplb);
            else
                rtnTempl += rtnTemplb;
            rtnTempl += rtnTemplf;

            
            return rtnTempl;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="info"></param>
        /// <param name="cartInfo"></param>
        /// <returns></returns>
        private ChronopostDefaults GetDefaultvalues(NBrightInfo info, NBrightInfo cartInfo)
        {
            var rtnData = new ChronopostDefaults();

            rtnData.accountnumber = info.GetXmlProperty("genxml/textbox/chronopostaccountnumber");
            rtnData.password = info.GetXmlProperty("genxml/textbox/chronopostpassword");

            rtnData.freeshippinglimit = info.GetXmlPropertyDouble("genxml/textbox/freeshippinglimit");

            rtnData.shipoption = cartInfo.GetXmlProperty("genxml/extrainfo/genxml/radiobuttonlist/rblshippingoptions");
            switch (rtnData.shipoption)
            {
                case "1":
                    rtnData.countrycode = cartInfo.GetXmlProperty("genxml/billaddress/genxml/dropdownlist/country");
                    rtnData.regionkey = cartInfo.GetXmlProperty("genxml/billaddress/genxml/dropdownlist/region");
                    rtnData.postalcode = cartInfo.GetXmlProperty("genxml/billaddress/genxml/textbox/postalcode");
                    break;
                case "2":
                    rtnData.countrycode = cartInfo.GetXmlProperty("genxml/shipaddress/genxml/dropdownlist/country");
                    rtnData.regionkey = cartInfo.GetXmlProperty("genxml/shipaddress/genxml/dropdownlist/region");
                    rtnData.postalcode = cartInfo.GetXmlProperty("genxml/shipaddress/genxml/textbox/postalcode");
                    break;
                default:
                    rtnData.countrycode = "";
                    rtnData.regionkey = "";
                    rtnData.postalcode = "";
                    break;
            }

            rtnData.productcode = cartInfo.GetXmlProperty("genxml/extrainfo/genxml/radiobuttonlist/chronopostproductcode");
            rtnData.distributioncountrycode = info.GetXmlProperty("genxml/textbox/distributioncountrycode");
            rtnData.distributioncountryname = info.GetXmlProperty("genxml/textbox/distributioncountryname");
            rtnData.distributionpostcode = info.GetXmlProperty("genxml/textbox/distributionpostcode");
            rtnData.leaddays = info.GetXmlProperty("genxml/textbox/chronopostleaddays");

            rtnData.printmode = info.GetXmlProperty("genxml/dropdownlist/printmode");

            if (rtnData.regionkey != "")
            {
                var rl = rtnData.regionkey.Split(':');
                if (rl.Count() == 2) rtnData.regioncode = rl[1];
            }

            // if it's only national deliver, use the postcode
            if (rtnData.countrycode == rtnData.distributioncountrycode)
            {
                rtnData.departurecode = rtnData.distributionpostcode;
                rtnData.arrivalcode = rtnData.postalcode;
            }
            else
            {
                rtnData.departurecode = rtnData.distributioncountrycode;
                rtnData.arrivalcode = rtnData.countrycode;
            }

            rtnData.datavalue = "";
            rtnData.data = "";

            var nodList = info.XMLDoc.SelectNodes("genxml/checkboxlist/chronopostproductcode/chk");
            if (nodList != null)
            {
                const string internationalcodes = "17;44;";
                foreach (XmlNode nod in nodList)
                {
                    if (nod.Attributes != null && nod.Attributes["value"].InnerText == "True")
                    {
                        var code = nod.Attributes["data"].InnerText + ";";
                        if (rtnData.countrycode != rtnData.distributioncountrycode)
                        {
                            // only allow international productcode.
                            if (internationalcodes.Contains(code))
                            {
                                rtnData.datavalue += code;
                                rtnData.data += nod.InnerText + ";";
                            }
                        }
                        else
                        {
                            // only alow national
                            if (!internationalcodes.Contains(code))
                            {
                                rtnData.datavalue += code;
                                rtnData.data += nod.InnerText + ";";
                            }
                        }
                    }
                }
                rtnData.datavalue = rtnData.datavalue.TrimEnd(';');
                rtnData.data = rtnData.data.TrimEnd(';');
            }


            // set the seleted value to the first valid porductcode in the list
            if (rtnData.productcode == "")
            {
                var s = rtnData.datavalue.Split(';');
                if (s.Any())
                {
                    rtnData.productcode =  s[0];
                    cartInfo.SetXmlProperty("genxml/extrainfo/genxml/radiobuttonlist/chronopostproductcode", s[0]);
                    var modCtrl = new NBrightBuyController();
                    modCtrl.Update(cartInfo); // update cart so the radiobutton pickup the default.
                }

            }

            rtnData.chronoposttype = info.GetXmlProperty("genxml/dropdownlist/chronoposttype");

            rtnData.totalweight = cartInfo.GetXmlPropertyDouble("genxml/totalweight");

            rtnData.totalcost = cartInfo.GetXmlPropertyDouble("genxml/total");

            return rtnData;
        }

        
    }

    /// <summary>
    /// Class to hold default
    /// </summary>
    public class ChronopostDefaults
    {
        public String data { get; set; }
        public String datavalue { get; set; }
        public String countrycode { get; set; }
        public String regioncode { get; set; }
        public String regionkey { get; set; }
        public String postalcode  { get; set; }
        public Double totalweight { get; set; }
        public Double totalcost { get; set; }

        public String shipoption { get; set; }

        public String accountnumber { get; set; }
        public String password { get; set; }
        public String chronoposttype { get; set; }
        public String leaddays { get; set; }

        public String productcode { get; set; }
        public String distributioncountrycode { get; set; }
        public String distributionpostcode { get; set; }

        public String printmode { get; set; }

        public String distributioncountryname { get; set; }

        public String departurecode { get; set; }
        public String arrivalcode { get; set; }

        public Double freeshippinglimit { get; set; }

    }


}
