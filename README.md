# Alma InnReach Relay
An ASP.NET MVC web service that facilitates NCIP communication between an InnReach DCB system and the Ex Libris Alma ILS.  Development of this solution was based off of the work done by Ian Chan at the California State University San Marcos Library.  [Alma Developer Network post](https://github.com/csusm-library/NCIP-Relay) | [GitHub repo](https://developers.exlibrisgroup.com/blog/NCIP-Relay-enabling-exchange-of-messages-between-INN-Reach-systems-and-Alma)

This is a model implementation only and does not have any event or error logging code.  It is not intended for direct use in production, but rather as a guide or starting off point for building your own production solution.

## Purpose
The design goal of this solution is to allow Sierra InnReach DCB systems to communicate with Alma over NCIP.  There are a number of changes that has to be made to an NCIP message from InnReach in order for Alma to process the request.  In addition to that, Alma does not implement the ItemCheckedOut or ItemRenewed NCIP services.  The impact of this is when an item is checked out to a patron or renewed in the DCB client Alma is not aware of the transaction.  In order for the item to be checked out to the patron in Alma, the checkout desk staff would have to check out the item in one system and then again in another. To work around this, when an ItemCheckedOut or ItemRenewed NCIP request is received by the relay it would process those request through the Alma API rather than NCIP.

## Deployment
This solution has been tested on Windows Server 2012 & IIS 8.  It should be deployed as a standard IIS Web Application.

There are a number of configuration fields in the web.config file that will need to be filled in with your institution-specific values.  Some of the values are pre-filled with These are:
- **AlmaNcipUrl:**  The url of the Alma NCIP responder.  InnReach operates on NCIP version 1, so the v1 responder should be used
- **InnReachSchemeTag:**  The value of the <scheme> element expected by InnReach.  Used to convert NCIP responses from Alma for InnReach
- **AlmaInstitutionCode:**  The institution code assigned to you by ExLibris
- **AlmaInstitutionName:**  The institution name according to ExLibris.
- **InnReachUserGroup:**  The patron type that will be sent to InnReach for LookUpUser NCIP service requests.  If you need to differentiate between different patrons in InnReach you will need to create your own mapping process to convert Alma user groups to InnReach patron types.
- **InnReachSiteCode:**  The code assigned to your institution by your InnReach library.
- **AlmaNcipProfileCode:**  The resource sharing partner code you define in Alma for this intigration.
- **UpgradeItemCheckOutRequest:**  If 'true' the relay service will attempt to use the Alma API to check out and renew items.  If false the relay service will use NCIP for those requests.  At the time of writing Alma did not implement the ItemCheckedOut and ItemRenewed NCIP services, so those messages would fail.
- **APICheckoutLibrary:**  The Alma library to use when checking out items through the API
- **APiCheckoutDesk:**  The Alma checkout desk to use when checkout out items through the API
- **CheckoutApIUrl:**  The Alma API request URI pattern that the relay will used to check out items.  You will need to replace {your api key here} with an API key you generate for the application in the Alma Developers Network.  the {0} and {1} will get replace at runtime with the user id and item barcode, respectively  with string.Replace().
- **ChangeDateApiUrl:**  The Alma API request URI pattern that the relay will use to set the due date of items at checkout time, as well as, when processing a renew.  As with the checkout URI pattern, you will need to replace {your api key here} with an API key you generate for the application in the Alma Developers Network.  the {0} and {1} will get replace at runtime with the user id and item barcode, respectively  with string.Replace()
- **GetLoansApiUrl:**  The Alma API request URI pattern that the relay will use to find the loan in Alma to renew when processing an ItemRenewed request.  You will have to fill in your api key, but the relay will fill in {0} with the user id at runtime.

## NCIP Message Modification
When processing an NCIP request the relay will modify the original request message from the InnReach system for Alma and the response from Alma will be modified to return to the InnReach system.  

### InnReach -> Alma
As described by Moshe Shechter in his post on the Alma Developer Network [here](https://developers.exlibrisgroup.com/blog/Alma-NCIP-Requirements-and-InnReach), the relay will swap out the InnReach site code in the <AgencyId> elements with the Alma institution code.  It will also add in the <ApplicationProfileType> element that contains the resource sharing partner code you define in Alma.  This information instructs Alma what customer the request is meant for and what resource sharing partner defined for that customer to use to process the request.

In testing the relay with our InnReach consortia borrowing library, we noticed that when a patron would request an item from their website they would have to type in their barcode.  We wanted our patrons to use a friendlier identifier that we also use as the primary identifier in Alma.  As a result, what the InnReach DCB system considers the barcode Alma considers the primary identifier.  The patron's barcode value according to Alma is stored in the "InnReach ID" index in DCB.  To implement this the relay will swap the UniqueUserId with the VisibleUserId in the NCIP message for each request those elements are present.

### Alma -> InnReach
To preare an NCIP response from Alma for an InnReach system, the relay will swap out the Alma institution code in the <AgencyId> element back to the InnReach site code.  The barcode <-> primary ID swapping also take place here, but in reverse.  Other cleanup such as reverting the <Scheme> element value and replacing the institution name with the InnReach site code happens here as well.

For certain NCIP service types, Alma will return a generic response message.  This was causing errors in the InnReach system.  e.g if InnReach sends an ItemCheckedOut request it expects an NCIP message with the <ItemCheckedOutResponse> element as the first child of the <NCIPMessage> root node, however Alma was sending a <Response> element instead.  To correct for this, the message type from the original request is used to replace the generic response element name.

In testing we also noticed that InnReach required patron email addresses to be sent differently than Alma was structuring them.  For NCIP responses that contain a patron email address, like the LookUpUserResponse, the email address will be moved to the correct element.

## Alma API for Checkout and Renewal
Alma does not implement the NCIP services required to check out an item to a patron.  When these NCIP messages are received by the relay, it can be configured to process those requests via the Alma API.  It is important to note that even though Alma sees the request come in through the API gateway, InnReach is still expecting an NCIP response message in return for those requests.  After sending the API request to Alma the relay will return the appropriate NCIP response message to InnReach.

A checkout event results in two different API requests.  There is no way to modify the due date of a loan when checking an item out through the API.  In order to respect the due dates on the InnReach checked out message, the relay will first perform the checkout in Alma then change the due date of that loan with a separate request.

For renew requests, the item is not renewed in Alma.  Rather as with the checkout, the due date of the loan is changed to the new due date in the checkout message.  Additionally, when an item is checked out in Alma, the system assigns a LoanId to that loan.  InnReach is not aware of this loan id value, so in order to discover it the relay will retrieve all of the given users active loans and try to match it up with the item barcode.  If it finds a current loan for an item with the specified barcode value it will change the due date of that loan.
