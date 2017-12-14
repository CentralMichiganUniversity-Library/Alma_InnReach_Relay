using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Xml;
using System.Net;
using System.IO;

namespace AlmaNcipRelay.Controllers
{
    [AllowAnonymous]
    public class NCIPRelayController : Controller
    {
        [AcceptVerbs(HttpVerbs.Get | HttpVerbs.Post), HandleError]
        public string Index()
        {
            string response = string.Empty;

            //read the NCIP package out of the request as an XMLDocument
            XmlDocument melRequest = new XmlDocument();
            melRequest.Load(Request.InputStream);

            //attempt to determine the type of NCIP request.  Different requests will be handled differently
            string requestType = string.Empty;
            XmlNode requestTypeNode = melRequest.SelectSingleNode("/NCIPMessage/*[1]");
            if (requestTypeNode != null)
            {
                requestType = requestTypeNode.Name;
            }

            //use the Alma APIs to process the message for ItemCheckedOut & ItemRenewed only if 
            //UpdageItemCheckOutRequest is set to 'true' in the web.config
            if (requestType == "ItemCheckedOut" && MvcApplication.UpgradeItemCheckOutRequest)
            {
                response = HandleItemCheckedOut(melRequest);
            }
            else if (requestType == "ItemRenewed" && MvcApplication.UpgradeItemCheckOutRequest)
            {
                response = HandleItemRenewed(melRequest);
            }
            else
            {
                response = HandleNcipRequest(melRequest, requestType);
            }

            Response.ContentType = "application/xml";
            return response;
        }

        /// <summary>
        /// Converts an NCIP request from the InnReach format to the Alma format and sends it to Alma.
        /// Then the responce from Alma is converted back to the InnReach format and returned
        /// </summary>
        /// <param name="melRequest">the InnReach NCIP package as an XMLDocument</param>
        /// <param name="requestType">The NCIP request type</param>
        /// <returns>The NCIP object to be returned to InnReach as an XML string</returns>
        private string HandleNcipRequest(XmlDocument melRequest, string requestType)
        {
            string melRequestString = XmlToString(melRequest);
            XmlDocument almaRequest = InnReachToAlma(melRequest, requestType);
            XmlDocument almaResponse = GetAlmaResponse(almaRequest);
            string almaResponseString = XmlToString(almaResponse);
            string melResponse = AlmaToInnReach(almaResponse, requestType);

            return melResponse;
        }


        /// <summary>
        /// Modifies the InnReach NCIP package to the format expected by Alma
        /// </summary>
        /// <param name="melRequest">The InnReach ncip package as an XMLDocument</param>
        /// <param name="requestType">The NCIP request type</param>
        /// <returns>NCIP package formatted for Alma as an XMLDocument</returns>
        private XmlDocument InnReachToAlma(XmlDocument melRequest, string requestType)
        {
            //replace all occurances of the innrech site code with the alma institution code
            string agencyCodeXpath = string.Format("//*[text() = '{0}']", MvcApplication.InnReachSiteCode);
            foreach (XmlNode agencyNode in melRequest.SelectNodes(agencyCodeXpath))
            {
                agencyNode.InnerText = MvcApplication.AlmaInstitutionCode;
            }

            //add in the ApplicationProfileType element
            var initHeaderNode = melRequest.SelectSingleNode("//InitiationHeader");
            if (initHeaderNode != null)
            {
                XmlElement appProfile = melRequest.CreateElement("ApplicationProfileType");
                initHeaderNode.AppendChild(appProfile);

                XmlElement scheme = melRequest.CreateElement("Scheme");
                scheme.SetAttribute("datatype", "string");
                scheme.InnerText = "http://www.niso.org/ncip/v1_0/imp1/dtd/ncip_v1_0.dtd?target=get_scheme_values&scheme=UniqueAgencyId";
                appProfile.AppendChild(scheme);

                XmlElement value = melRequest.CreateElement("Value");
                value.SetAttribute("datatype", "string");
                value.InnerText = MvcApplication.AlmaNcipProfileCode;
                appProfile.AppendChild(value);


            }

            if (requestType == "LookupUser")
            {
                //add in the UniqueUserId element
                var lookupBarcode = melRequest.SelectSingleNode("/NCIPMessage/LookupUser/VisibleUserId/VisibleUserIdentifier");
                if (lookupBarcode != null)
                {
                    var lookupNode = lookupBarcode.ParentNode.ParentNode;
                    XmlElement uniqId = melRequest.CreateElement("UniqueUserId");
                    lookupNode.AppendChild(uniqId);

                    XmlElement agaencyId = melRequest.CreateElement("UniqueAgencyId");
                    uniqId.AppendChild(agaencyId);

                    XmlElement scheme = melRequest.CreateElement("Scheme");
                    scheme.SetAttribute("datatype", "string");
                    scheme.InnerText = MvcApplication.InnReachUserIdSchemeTag;
                    agaencyId.AppendChild(scheme);

                    XmlElement value = melRequest.CreateElement("Value");
                    value.SetAttribute("datatype", "string");
                    value.InnerText = MvcApplication.AlmaInstitutionCode;
                    agaencyId.AppendChild(value);

                    XmlElement idValue = melRequest.CreateElement("UserIdentifierValue");
                    idValue.SetAttribute("datatype", "string");
                    idValue.InnerText = lookupBarcode.InnerText;
                    uniqId.AppendChild(idValue);

                }

            }
            else if (requestType == "AcceptItem")
            {
                //switch the unique userid with the barcode value
                var idNode = melRequest.SelectSingleNode("/NCIPMessage/AcceptItem/UniqueUserId/UserIdentifierValue");
                var barNode = melRequest.SelectSingleNode("/NCIPMessage/AcceptItem/UserOptionalFields/VisibleUserId/VisibleUserIdentifier");
                if (!(idNode == null || barNode == null))
                {
                    idNode.InnerText = barNode.InnerText;
                }
            }
            return melRequest;
        }

        /// <summary>
        /// Sends sends the NCIP package to the Alma NCIP endpoint and returns the response as an XMLDocument
        /// </summary>
        /// <param name="almaRequest">The NCIP package formatted for Alma</param>
        /// <returns>The NCIP response as an XMLDocument</returns>
        private XmlDocument GetAlmaResponse(XmlDocument almaRequest)
        {
            XmlDocument almaResponce = new XmlDocument();

            HttpWebRequest almaNcip = (HttpWebRequest)WebRequest.Create(MvcApplication.AlmaNcipUrl);
            almaNcip.ContentType = "application/xml";
            almaNcip.Method = "POST";

            using (var requestStream = almaNcip.GetRequestStream())
            {
                byte[] postData = System.Text.Encoding.UTF8.GetBytes(XmlToString(almaRequest));
                requestStream.Write(postData, 0, postData.Length);
            }

            using (HttpWebResponse ncipResponse = (HttpWebResponse)almaNcip.GetResponse())
            using (StreamReader reader = new StreamReader(ncipResponse.GetResponseStream()))
            {
                almaResponce.Load(reader);
            }

            return almaResponce;
        }

        /// <summary>
        /// Converts the NCIP resposne from Alma to the formate expected by InnReach
        /// </summary>
        /// <param name="almaResponse">Alma NCIP response package an an XMLDocument</param>
        /// <param name="requestType">NCIP request type</param>
        /// <returns>NCIP response formatted for InnReach as a string</returns>
        private string AlmaToInnReach(XmlDocument almaResponse, string requestType)
        {
            // replace the alma institution code with the InnReach code
            string agencyCodeXpath = string.Format("//*[text() = '{0}']", MvcApplication.AlmaInstitutionCode);
            foreach (XmlNode agencyNode in almaResponse.SelectNodes(agencyCodeXpath))
            {
                agencyNode.InnerText = MvcApplication.InnReachSiteCode;
            }

            // replace the alma institution name with the InnReach code
            string nameXpath = string.Format("//Scheme[contains(text(),'{0}')]|//UniqueAgencyId/Value[contains(text(),'{0}')]", MvcApplication.AlmaInstitutionName);
            foreach (XmlNode nameNode in almaResponse.SelectNodes(nameXpath))
            {
                nameNode.InnerText = nameNode.InnerText.Replace(MvcApplication.AlmaInstitutionName, MvcApplication.InnReachSiteCode);
            }

            //correct the agency id scheme tag
            foreach (XmlNode scheme in almaResponse.SelectNodes("//Scheme[text() = 'NCIP Unique Agency Id']"))
            {
                scheme.InnerText = MvcApplication.InnReachSchemeTag;
            }

            //fix generic response tags.  These cause an error on the InnReach side.
            XmlNode genericResponseNode = almaResponse.SelectSingleNode("/NCIPMessage/Response");
            if (genericResponseNode != null)
            {
                XmlNode typeNode = almaResponse.CreateElement(requestType + "Response");
                while (genericResponseNode.HasChildNodes)
                {
                    typeNode.AppendChild(genericResponseNode.FirstChild);
                }

                var parent = genericResponseNode.ParentNode;
                parent.InsertBefore(typeNode, genericResponseNode);
                parent.RemoveChild(genericResponseNode);
            }

            //format the patron email address for InnReach 
            XmlNode emailNode = almaResponse.SelectSingleNode("/NCIPMessage/LookupUserResponse/UserOptionalFields/UserAddressInformation/ElectronicAddress");
            if (emailNode != null)
            {
                var userFieldsNode = emailNode.ParentNode.ParentNode;

                XmlElement addressInfo = almaResponse.CreateElement("UserAddressInformation");
                userFieldsNode.AppendChild(addressInfo);

                XmlElement elecAddress = almaResponse.CreateElement("ElectronicAddress");
                addressInfo.AppendChild(elecAddress);

                XmlElement elecAddressType = almaResponse.CreateElement("ElectronicAddressType");
                elecAddress.AppendChild(elecAddressType);

                XmlElement scheme = almaResponse.CreateElement("Scheme");
                scheme.InnerText = "http://www.iana.org/assignments/uri-schemes.html";
                elecAddressType.AppendChild(scheme);

                XmlElement value = almaResponse.CreateElement("Value");
                value.InnerText = "mailto";
                elecAddressType.AppendChild(value);

                XmlElement emailValue = almaResponse.CreateElement("ElectronicAddressData");
                emailValue.InnerText = emailNode.InnerText;
                elecAddress.AppendChild(emailValue);

                userFieldsNode.RemoveChild(emailNode.ParentNode);
            }

            //replace the alma primary ID with the Barcode
            XmlNode idNode = almaResponse.SelectSingleNode("/NCIPMessage/LookupUserResponse/UniqueUserId/UserIdentifierValue");
            if (idNode != null)
            {
                XmlNode barnode = almaResponse.SelectSingleNode("/NCIPMessage/LookupUserResponse/UserOptionalFields/VisibleUserId[VisibleUserIdentifierType/Value[text() = 'BARCODE']]/VisibleUserIdentifier");
                if (barnode != null)
                {
                    idNode.InnerText = barnode.InnerText;
                }
            }

            //use the hard coded user group.
            XmlNode userGroupNode = almaResponse.SelectSingleNode("/NCIPMessage/LookupUserResponse/UserOptionalFields/UserPrivilege/AgencyUserPrivilegeType/Value");
            if (userGroupNode != null)
            {
                userGroupNode.InnerText = MvcApplication.InnReachUserGroup;
            }

            return XmlToString(almaResponse);
        }

        /// <summary>
        /// Attempt to check out the item in the InnReach ItemCheckedOut request using the Alma API
        /// Constructs an ItemCheckedOut NCIP response to return to InnReach.
        /// 
        /// Makes two sepperate API calls.  Once to create the loan and another to set the due date.
        /// </summary>
        /// <param name="melRequest">ItemCheckedOut request as an XMLDocument</param>
        /// <returns>NCIP success message OR error message if there is an error</returns>
        private string HandleItemCheckedOut(XmlDocument melRequest)
        {
            string response = string.Empty, apiResponse = string.Empty;
            string apiUrl = LoanUrlFromNCip(melRequest);
            HttpWebRequest almaNcip = (HttpWebRequest)WebRequest.Create(apiUrl);
            almaNcip.ContentType = "application/xml";
            almaNcip.Method = "POST";

            using (var requestStream = almaNcip.GetRequestStream())
            {
                byte[] postData = System.Text.Encoding.UTF8.GetBytes(string.Format(CHECKOUT_LOAN_OBJECT, MvcApplication.APiCheckoutDesk, MvcApplication.APICheckoutLibrary));
                requestStream.Write(postData, 0, postData.Length);
            }

            try
            {
                using (HttpWebResponse ncipResponse = (HttpWebResponse)almaNcip.GetResponse())
                using (StreamReader reader = new StreamReader(ncipResponse.GetResponseStream()))
                {
                    apiResponse = reader.ReadToEnd();
                    response = string.Format(NCIP_RESPONSE_SUCCESS, MvcApplication.InnReachSiteCode, "ItemCheckedOut");
                }
            }
            catch (WebException ex)
            {
                //TODO add any other error logging code here
                if (ex.Response != null)
                {
                    using (HttpWebResponse errorResponse = (HttpWebResponse)ex.Response)
                    using (StreamReader errorReader = new StreamReader(ex.Response.GetResponseStream()))
                    {
                        apiResponse = errorReader.ReadToEnd();
                    }
                }
                response = string.Format(NCIP_RESPONSE_PROBLEM, MvcApplication.InnReachSiteCode, "ItemCheckedOut");
            }

            string dueDateString = null;
            var dueDateNode = melRequest.SelectSingleNode("/NCIPMessage/ItemCheckedOut/DateDue");
            if (dueDateNode != null)
            {
                dueDateString = dueDateNode.InnerText;
            }

            if (!string.IsNullOrWhiteSpace(dueDateString) && apiResponse.Contains("loan_id"))
            {
                XmlDocument loan = new XmlDocument();
                loan.LoadXml(apiResponse);
                string changeUrl = ChangeUrlFromNcip(loan);
                apiUrl += Environment.NewLine + Environment.NewLine + changeUrl;
                apiResponse += Environment.NewLine + Environment.NewLine + ChangeDueDate(loan, changeUrl, dueDateString);
            }

            return response;
        }

        /// <summary>
        /// Changes the due date of a loan referenced in the ItemRenewed NCIP message
        /// </summary>
        /// <param name="melRequest">ItemRenewed ncip request as an XMLDocument</param>
        /// <returns>NCIP success message OR error message if there is an error</returns>
        private string HandleItemRenewed(XmlDocument melRequest)
        {
            string response = string.Format(NCIP_RESPONSE_PROBLEM, MvcApplication.InnReachSiteCode, "ItemRenewed");
            string apiUrl = "", apiResponse = "";

            string dueDateString = null;
            var dueDateNode = melRequest.SelectSingleNode("/NCIPMessage/ItemRenewed/DateDue");
            if (dueDateNode != null)
            {
                dueDateString = dueDateNode.InnerText;
            }

            if (!string.IsNullOrWhiteSpace(dueDateString))
            {
                XmlDocument loan = GetLoanObject(melRequest);

                if (loan != null)
                {
                    string changeUrl = ChangeUrlFromNcip(loan);
                    apiUrl = changeUrl;
                    apiResponse = ChangeDueDate(loan, changeUrl, dueDateString);
                    response = string.Format(NCIP_RESPONSE_SUCCESS, MvcApplication.InnReachSiteCode, "ItemRenewed");
                }
            }

            return response;
        }

        /// <summary>
        /// Search the patrons active loans to find the loan referenced by an ItemRenewed ncip reqeust
        /// </summary>
        /// <param name="melRequest">ItemRenewed NCIP request as an XMLDocument</param>
        /// <returns>Alma loan object as an XMLDocument</returns>
        private XmlDocument GetLoanObject(XmlDocument melRequest)
        {
            XmlDocument loan = null, userLoans = null;

            var barNode = melRequest.SelectSingleNode("/NCIPMessage/ItemRenewed/ItemOptionalFields/ItemDescription/VisibleItemId/VisibleItemIdentifier[../VisibleItemIdentifierType/Value[text() = 'Barcode']]");
            var userNode = melRequest.SelectSingleNode("/NCIPMessage/ItemRenewed/UniqueUserId/UserIdentifierValue");

            if (!(barNode == null || userNode == null || string.IsNullOrWhiteSpace(barNode.InnerText)))
            {
                HttpWebRequest almaNcip = (HttpWebRequest)WebRequest.Create(string.Format(MvcApplication.GetLoansApiUrl, userNode.InnerText));
                almaNcip.Method = "GET";

                using (HttpWebResponse ncipResponse = (HttpWebResponse)almaNcip.GetResponse())
                using (StreamReader reader = new StreamReader(ncipResponse.GetResponseStream()))
                {
                    userLoans = new XmlDocument();
                    userLoans.LoadXml(reader.ReadToEnd());

                    var melLoan = userLoans.SelectSingleNode(string.Format("/item_loans/item_loan[item_barcode = '{0}']", barNode.InnerText));
                    if (melLoan != null)
                    {
                        loan = new XmlDocument();
                        XmlElement root = loan.CreateElement("item_loan");
                        loan.AppendChild(root);
                        root.InnerXml = melLoan.InnerXml;

                    }

                }

            }


            return loan;
        }

        
        /// <summary>
        /// Changes the due date of an Alma loan to the date specified.
        /// 
        /// Used for checking out and renewing items
        /// </summary>
        /// <param name="loan">Alma loan object</param>
        /// <param name="apiUrl">Complete API url with apiKey & other parameters set</param>
        /// <param name="due">loan due date</param>
        /// <returns>the Alma API resposne string</returns>
        private string ChangeDueDate(XmlDocument loan, string apiUrl, string due)
        {
            string apiResponse = "";

            var dateNode = loan.SelectSingleNode("/item_loan/due_date");
            if (dateNode != null)
            {
                dateNode.InnerText = due;

                HttpWebRequest almaNcip = (HttpWebRequest)WebRequest.Create(apiUrl);
                almaNcip.ContentType = "application/xml";
                almaNcip.Method = "PUT";

                using (var requestStream = almaNcip.GetRequestStream())
                {
                    //.NET internally represents strings as utf-16.  Alma did not like that.
                    byte[] postData = System.Text.Encoding.UTF8.GetBytes(XmlToString(loan).Replace("<?xml version=\"1.0\" encoding=\"utf-16\"?>", "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"));
                    requestStream.Write(postData, 0, postData.Length);
                }

                try
                {
                    using (HttpWebResponse ncipResponse = (HttpWebResponse)almaNcip.GetResponse())
                    using (StreamReader reader = new StreamReader(ncipResponse.GetResponseStream()))
                    {
                        apiResponse = reader.ReadToEnd();
                    }
                }
                catch (WebException ex)
                {
                    //TODO add any other error logging code here
                    if (ex.Response != null)
                    {
                        using (HttpWebResponse errorResponse = (HttpWebResponse)ex.Response)
                        using (StreamReader errorReader = new StreamReader(ex.Response.GetResponseStream()))
                        {
                            apiResponse = errorReader.ReadToEnd();
                        }
                    }
                }

            }

            return apiResponse;
        }

        /// <summary>
        /// Creates the Alma API url for changing the due date of the given Alma loan object
        /// </summary>
        /// <param name="loan">Alma loan object</param>
        /// <returns>The complete API request string</returns>
        private string ChangeUrlFromNcip(XmlDocument loan)
        {

            var loanNode = loan.SelectSingleNode("/item_loan/loan_id");
            var userNode = loan.SelectSingleNode("/item_loan/user_id");
            if (!(loanNode == null || userNode == null))
            {
                return string.Format(MvcApplication.ChangeDateApiUrl, userNode.InnerText, loanNode.InnerText);
            }
            return "";
        }

        /// <summary>
        /// Creates the API checkout URL for an ItemCheckedOut ncip request.
        /// 
        /// A checkout API url must have a user identifier and an item identifier in the query string.
        /// </summary>
        /// <param name="melRequest">The ItemCheckedOut ncip package</param>
        /// <returns>complete Alma API request url</returns>
        private string LoanUrlFromNCip(XmlDocument melRequest)
        {
            string userId = string.Empty;
            XmlNode userNode = melRequest.SelectSingleNode("/NCIPMessage/ItemCheckedOut/UniqueUserId/UserIdentifierValue");
            if (userNode != null)
            {
                userId = userNode.InnerText;
            }

            string barcode = null;
            XmlNode barNode = melRequest.SelectSingleNode("/NCIPMessage/ItemCheckedOut/ItemOptionalFields/ItemDescription/VisibleItemId/VisibleItemIdentifier[../VisibleItemIdentifierType/Value[text() = 'Barcode']]");
            if (barNode != null)
            {
                barcode = barNode.InnerText;
            }

            string itemId = string.Empty;
            XmlNode itemNode = melRequest.SelectSingleNode("/NCIPMessage/ItemCheckedOut/UniqueItemId/ItemIdentifierValue");
            if (itemNode != null)
            {
                itemId = itemNode.InnerText;
            }

            return string.Format(MvcApplication.CheckoutApIUrl, userId, barcode ?? itemId);
        }


        /// <summary>
        /// Converts an XMLDocument to its string representation.
        /// </summary>
        /// <param name="doc">XMLDocument to be serialiazed as a string</param>
        /// <returns>string representation of docment</returns>
        private string XmlToString(XmlDocument doc)
        {
            string xml = string.Empty;
            XmlWriterSettings settings = new XmlWriterSettings();

            using (var stringWriter = new StringWriter())
            using (var xmlTextWriter = XmlWriter.Create(stringWriter))
            {
                doc.WriteTo(xmlTextWriter);
                xmlTextWriter.Flush();
                xml = stringWriter.GetStringBuilder().ToString();
            }
            return xml;
        }


        private const string NCIP_RESPONSE_SUCCESS =
            @"<?xml version=""1.0"" encoding=""UTF-8""?>
                <!DOCTYPE NCIPMessage PUBLIC ""-//NISO//NCIP DTD Version 1//EN"" ""http://www.niso.org/ncip/v1_0/imp1/dtd/ncip_v1_0.dtd"">
                <NCIPMessage version = ""http://www.niso.org/ncip/v1_0/imp1/dtd/ncip_v1_0.dtd"" >
                    <{1}Response>
                        <ResponseHeader>
                            <FromAgencyId>
                                <UniqueAgencyId>
                                    <Scheme > http://72.52.134.169:6601/IRCIRCD?target=get_scheme_values&amp;scheme=UniqueAgencyId</Scheme>
					                <Value>{0}</Value>
				                </UniqueAgencyId>
			                </FromAgencyId>
			                <ToAgencyId>
				                <UniqueAgencyId>
					                <Scheme>http://72.52.134.169:6601/IRCIRCD?target=get_scheme_values&amp;scheme=UniqueAgencyId</Scheme>
					                <Value>{0}</Value>
				                </UniqueAgencyId>
			                </ToAgencyId>
		                </ResponseHeader>
	                </{1}Response>
                </NCIPMessage> ";

        private const string CHECKOUT_LOAN_OBJECT =
            @"<?xml version=""1.0"" encoding=""UTF-8""?><item_loan><circ_desk>{0}</circ_desk><library>{1}</library></item_loan>";

        private const string NCIP_RESPONSE_PROBLEM =
            @"<?xml version=""1.0"" encoding=""UTF-8""?>
                <!DOCTYPE NCIPMessage PUBLIC ""-//NISO//NCIP DTD Version 1//EN"" ""http://www.niso.org/ncip/v1_0/imp1/dtd/ncip_v1_0.dtd"">
                <NCIPMessage version = ""http://www.niso.org/ncip/v1_0/imp1/dtd/ncip_v1_0.dtd"" >
                    <{1}Response >
                        <ResponseHeader >
                            <FromAgencyId >
                                <UniqueAgencyId >
                                    <Scheme > http://72.52.134.169:6601/IRCIRCD?target=get_scheme_values&amp;scheme=UniqueAgencyId</Scheme>
					                <Value>{0}</Value>
				                </UniqueAgencyId>
			                </FromAgencyId>
			                <ToAgencyId>
				                <UniqueAgencyId>
					                <Scheme>http://72.52.134.169:6601/IRCIRCD?target=get_scheme_values&amp;scheme=UniqueAgencyId</Scheme>
					                <Value>{0}</Value>
				                </UniqueAgencyId>
			                </ToAgencyId>
		                </ResponseHeader>
                        <Problem>
			                <ErrorCode>0104 - NCIP Parse Error</ErrorCode>
			                <ErrorMessage>Amla API returned an error state</ErrorMessage>
		                </Problem>
	                </{1}Response>
                </NCIPMessage> ";
    }
}