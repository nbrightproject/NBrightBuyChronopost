// --- Copyright (c) notice NevoWeb ---
//  Copyright (c) 2014 SARL NevoWeb.  www.nevoweb.com.
// Author: D.C.Lee
// ------------------------------------------------------------------------
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED
// TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
// THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF
// CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.
// ------------------------------------------------------------------------
// This copyright notice may NOT be removed, obscured or modified without written consent from the author.
// --- End copyright notice --- 

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web;
using System.Web.UI.WebControls;
using DotNetNuke.Common;
using DotNetNuke.Entities.Portals;
using NBrightCore.common;
using NBrightCore.render;
using NBrightDNN;

using Nevoweb.DNN.NBrightBuy.Base;
using Nevoweb.DNN.NBrightBuy.Components;
using DataProvider = DotNetNuke.Data.DataProvider;

namespace Nevoweb.DNN.NBrightBuyChronopost
{

    /// -----------------------------------------------------------------------------
    /// <summary>
    /// The ViewNBrightGen class displays the content
    /// </summary>
    /// -----------------------------------------------------------------------------
    public partial class Chronopost : NBrightBuyAdminBase
    {


        #region Event Handlers

        private String _ctrlkey = "";
        private NBrightInfo _info;
        private NBrightBuyController _modCtrl;

        override protected void OnInit(EventArgs e)
        {
            base.OnInit(e);

            try
            {
                _modCtrl = new NBrightBuyController();

                _ctrlkey = (String)HttpContext.Current.Session["nbrightbackofficectrl"];

                #region "load templates"

                var t1 = "shippingheader.html";

                // Get Display Header
                var rpDataHTempl = GetTemplateData(t1);
                rpDataH.ItemTemplate = NBrightBuyUtils.GetGenXmlTemplate(rpDataHTempl, StoreSettings.Current.Settings(), PortalSettings.HomeDirectory);

                #endregion


            }
            catch (Exception exc)
            {
                //display the error on the template (don;t want to log it here, prefer to deal with errors directly.)
                var l = new Literal();
                l.Text = exc.ToString();
                Controls.Add(l);
            }

        }

        protected override void OnLoad(EventArgs e)
        {
            try
            {
                base.OnLoad(e);

                _info = _modCtrl.GetByGuidKey(PortalSettings.Current.PortalId, -1, "SHIPPING", _ctrlkey);

                if (_info == null)
                {
                    _info = new NBrightInfo(true);
                    _info.GUIDKey = _ctrlkey;
                    _info.TypeCode = "SHIPPING";
                    _info.ModuleId = -1;
                    _info.PortalId = PortalSettings.Current.PortalId;
                }


                if (Page.IsPostBack == false)
                {
                    PageLoad();
                }
            }
            catch (Exception exc) //Module failed to load
            {
                //display the error on the template (don;t want to log it here, prefer to deal with errors directly.)
                var l = new Literal();
                l.Text = exc.ToString();
                Controls.Add(l);
            }
        }

        private void PageLoad()
        {
            if (UserId > 0) // only logged in users can see data on this module.
            {
                // display header
                base.DoDetail(rpDataH,_info);

            }
        }

        #endregion

        #region  "Events "

        protected void CtrlItemCommand(object source, RepeaterCommandEventArgs e)
        {
            var cArg = e.CommandArgument.ToString();
            var param = new string[3];

            switch (e.CommandName.ToLower())
            {
                case "save":
                    Update();
                    Response.Redirect(Globals.NavigateURL(TabId, "", param), true);
                    break;
                case "cancel":
                    Response.Redirect(Globals.NavigateURL(TabId, "", param), true);
                    break;
            }

        }

        #endregion

        private String GetTemplateData(String templatename)
        {
            var controlMapPath = HttpContext.Current.Server.MapPath("/DesktopModules/NBright/NBrightBuyChronopost");
            var templCtrl = new NBrightCore.TemplateEngine.TemplateGetter(PortalSettings.Current.HomeDirectoryMapPath, controlMapPath, "Themes\\config", "");
            var templ = templCtrl.GetTemplateData(templatename, Utils.GetCurrentCulture());
            templ = Utils.ReplaceSettingTokens(templ, StoreSettings.Current.Settings());
            templ = Utils.ReplaceUrlTokens(templ);
            return templ;
        }

        private void Update()
        {

            var strXml = GenXmlFunctions.GetGenXml(rpDataH);
            _info.XMLData = strXml;
            _modCtrl.Update(_info);

            //remove current setting from cache for reload
            Utils.RemoveCache("NBrightBuyChronopostShipping" + PortalSettings.Current.PortalId.ToString(""));

        }




    }

}
