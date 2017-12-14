using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;
using System.Configuration;

namespace AlmaNcipRelay
{
    public class MvcApplication : System.Web.HttpApplication
    {
        public static string InnReachSiteCode { get; private set; }
        public static string AlmaInstitutionCode { get; private set; }
        public static string AlmaInstitutionName { get; set; }
        public static string InnReachSchemeTag { get; private set; }
        public static string AlmaNcipUrl { get; private set; }
        public static string AlmaNcipProfileCode { get; private set; }
        public static string InnReachUserGroup { get; private set; }
        public static bool UpgradeItemCheckOutRequest { get; private set; }
        public static string APICheckoutLibrary { get; private set; }
        public static string APiCheckoutDesk { get; private set; }
        public static string CheckoutApIUrl { get; private set; }
        public static string ChangeDateApiUrl { get; private set; }
        public static string GetLoansApiUrl { get; private set; }
        public static string InnReachUserIdSchemeTag { get; private set; }
        protected void Application_Start()
        {
            AreaRegistration.RegisterAllAreas();
            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            RouteConfig.RegisterRoutes(RouteTable.Routes);

            InnReachSiteCode = ConfigurationManager.AppSettings["InnReachSiteCode"];
            AlmaInstitutionCode = ConfigurationManager.AppSettings["AlmaInstitutionCode"];
            InnReachSchemeTag = ConfigurationManager.AppSettings["InnReachSchemeTag"];
            AlmaNcipUrl = ConfigurationManager.AppSettings["AlmaNcipUrl"];
            AlmaInstitutionName = ConfigurationManager.AppSettings["AlmaInstitutionName"];
            AlmaNcipProfileCode = ConfigurationManager.AppSettings["AlmaNcipProfileCode"];
            InnReachUserGroup = ConfigurationManager.AppSettings["InnReachUserGroup"];
            UpgradeItemCheckOutRequest = ConfigurationManager.AppSettings["UpgradeItemCheckOutRequest"] == "true";
            APICheckoutLibrary = ConfigurationManager.AppSettings["ApiCheckoutLibrary"];
            APiCheckoutDesk = ConfigurationManager.AppSettings["ApiCheckoutDesk"];
            CheckoutApIUrl = ConfigurationManager.AppSettings["CheckoutApIUrl"];
            ChangeDateApiUrl = ConfigurationManager.AppSettings["ChangeDateApiUrl"];
            GetLoansApiUrl = ConfigurationManager.AppSettings["GetLoansApiUrl"];
            InnReachUserIdSchemeTag = ConfigurationManager.AppSettings["InnReachUserIdSchemeTag"];
        }
    }
}
