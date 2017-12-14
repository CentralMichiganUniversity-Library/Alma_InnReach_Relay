# Alma INN-Reach DCB Relay
An ASP.NET MVC web service that facilitates NCIP communication between an INN-Reach DCB system and the Ex Libris Alma ILS.  Development of this solution was based off of the work done by Ian Chan at the California State University San Marcos Library.  [Alma Developer Network post](https://github.com/csusm-library/NCIP-Relay) | [GitHub repo](https://developers.exlibrisgroup.com/blog/NCIP-Relay-enabling-exchange-of-messages-between-INN-Reach-systems-and-Alma)

This is a model implementation only and does not have any event or error logging code.  It is not intended for direct use in production, but rather as a guide or starting off point for building your own production solution.

## Background
The design goal of this solution is to allow Innovative Interfaces INN-Reach DCB systems to communicate with Alma over NCIP.  Central Michigan University Library developed this solution as part of the library's migration from III Sierra to Ex Libris Alma.  CMU participates in a state-wide interlibrary loan network organized by the Library of Michigan called [Michigan eLibrary](http://mel.org/) (MeL). MeL uses III INN-Reach as the system to coordinate the resource sharing.  The goal of this solution was to allow CMU to participate in MeL with Alma as seamlessly they were able to with III Sierra.   III INN-Reach will communicate natively with other III ILS systems, but it can also communicate to other ILSs via NCIP through its Direct Consortial Borrowing (DCB) system.

 There are a number of changes that have to be made to an NCIP message from DCB in order for Alma to process the request.  In addition to that, Alma does not implement the ItemCheckedOut or ItemRenewed NCIP services.  The impact of this is when an item is checked out to a patron or renewed in the DCB client Alma is not aware of the transaction. The impact of this is in order for the item to be checked out to the patron in Alma, the checkout desk staff would have to check out the item in one system and then again in another. To work around this, when an ItemCheckedOut or ItemRenewed NCIP request is received by the relay it would process those requests through the Alma API rather than NCIP. This allows a checkout or renew in the DCB system to be communicated to Alma directly without the checkout desk staff duplicating effort.

## Deployment
This solution has been tested on Windows Server 2012 R2 & IIS 8.  It should be deployed as a standard IIS Web Application.

There are a number of configuration fields in the web.config file that will need to be filled in with your institution-specific values.  Some of the settings are pre-filled with examples.

- **AlmaNcipUrl:**  The URL of the Alma NCIP responder.  DCB operates on NCIP version 1, so the v1 responder should be used.
- **InnReachAgencyIdSchemeTag:**  The value of the Scheme element expected by DCB for interpreting the agency id.  Used to convert NCIP responses from Alma for DCB.  The address in the URL should be the address of the DCB system.
- **InnReachUserIdSchemeTag:**  The value of the Scheme element expected by DCB for inturpreting the user id.  Used to convert NCIP responses from Alma for DCB.  The address in the URL should be the address of the DCB system.
- **AlmaInstitutionCode:**  The institution code assigned to you by Ex Libris.
- **AlmaInstitutionName:**  The institution name according to Ex Libris.
- **InnReachUserGroup:**  The patron type that will be sent to DCB for LookUpUser NCIP service requests.  If you need to differentiate between different patrons in DCB you will need to create your own mapping process to convert Alma user groups to DCB patron types.
- **InnReachSiteCode:**  The code assigned to your institution by your DCB library.  Also referred to as the Agency ID.
- **AlmaNcipProfileCode:**  The resource sharing partner code you define in Alma for this integration.
- **UpgradeItemCheckOutRequest:**  If 'true' the relay service will attempt to use the Alma API to check out and renew items.  If false the relay service will use NCIP for those requests.  At the time of writing Alma did not implement the ItemCheckedOut and ItemRenewed NCIP services, so those messages would fail.
- **ApiCheckoutLibrary:**  The Alma library to use when checking out items through the API.
- **ApiCheckoutDesk:**  The Alma checkout desk to use when checking out items through the API.
- **CheckoutApiUrl:**  The Alma API request URL pattern that the relay will use to check out items.  You will need to replace {your API key here} with an API key you generate for the application in the Alma Developers Network.  The {0} and {1} will get replaced at runtime with the user id and item barcode, respectively with string.Replace().
- **ChangeDateApiUrl:**  The Alma API request URL pattern that the relay will use to set the due date of items at checkout time, as well as, when processing a renew.  As with the checkout URI pattern, you will need to replace {your api key here} with an API key you generate for the application in the Alma Developers Network.  the {0} and {1} will get replaced at runtime with the user id and item barcode, respectively  with string.Replace().
- **GetLoansApiUrl:**  The Alma API request URL pattern that the relay will use to find the loan in Alma to renew when processing an ItemRenewed request.  You will have to fill in your api key, but the relay will fill in {0} with the user id at runtime.

## NCIP Message Modification
When processing an NCIP request the relay will modify the original request message from the DCB system for Alma and the response from Alma will be modified to return to the DCB system.  

### DCB -> Alma
As described by Moshe Shechter in his post on the Alma Developer Network [here](https://developers.exlibrisgroup.com/blog/Alma-NCIP-Requirements-and-InnReach), the relay will swap out the DCB site code in the AgencyId elements with the Alma institution code.  It will also add in the ApplicationProfileType element that contains the resource sharing partner code you define in Alma.  This information instructs Alma what customer the request is meant for and what resource sharing partner defined for that customer to use to process the request.

In testing the relay with MeL, we noticed that when a patron would request an item from the MeLCat website they would have to type in their barcode.  We wanted our patrons to use a friendlier identifier that we also use as the primary identifier in Alma.  As a result, what the DCB system considers the barcode Alma considers the primary identifier.  The patron's barcode value according to Alma is stored in the "NCIP ID" index in DCB.  To implement this the relay will swap the UniqueUserId with the VisibleUserId in the NCIP message for each request those elements are present.

### Alma -> DCB
To prepare an NCIP response from Alma for a DCB system, the relay will swap out the Alma institution code in the AgencyId element back to the DCB site code.  The barcode <-> primary ID swapping also takes place here, but in reverse.  Other cleanup such as reverting the Scheme element value and replacing the institution name with the DCB site code happens here as well.

For certain NCIP service types, Alma will return a generic response message.  This was causing errors in the DCB system.  e.g if DCB sends an ItemCheckedOut request it expects an NCIP message with the ItemCheckedOutResponse element as the first child of the NCIPMessage root node, however Alma was sending a Response element instead.  To correct for this, the message type from the original request is used to replace the generic response element name.

In testing we also noticed that DCB required patron email addresses to be sent differently than Alma was structuring them.  For NCIP responses that contain a patron email address, like the LookUpUserResponse, the email address will be moved to the correct element.

## Alma API for Checkout and Renewal
Alma does not implement the NCIP services required to check out an item to a patron.  When these NCIP messages are received by the relay, it can be configured to process those requests via the Alma API.  It is important to note that even though Alma sees the request come in through the API gateway, DCB is still expecting an NCIP response message in return for those requests.  After sending the API request to Alma the relay will return the appropriate NCIP response message to DCB.

A checkout event results in two different API requests.  There is no way to modify the due date of a loan when checking an item out through the API.  In order to respect the due dates on the DCB checked out message, the relay will first perform the checkout in Alma then change the due date of that loan with a separate request.

For renew requests, the item is not renewed in Alma.  Rather as with the checkout, the due date of the loan is changed to the new due date in the checkout message.  Additionally, when an item is checked out in Alma, the system assigns a LoanId to that loan.  DCB is not aware of this loan id value, so in order to discover it the relay will retrieve all of the given users active loans and try to match it up with the item barcode.  If it finds a current loan for an item with the specified barcode value it will change the due date of that loan.

### Known Issues
So far there are two issues with the way the relay uses the [Change Loan Due Date API](https://developers.exlibrisgroup.com/alma/apis/users/PUT/gwPcGly021r0XQMGAttqcPPFoLNxBoEZNUiGwQUr+MuAI+35dTBcVUmYayAq/vUq/0aa8d36f-53d6-48ff-8996-485b90b103e4) in Alma.  The impact is some confusion for patrons when items are checked out or renewed:
1.  The loan receipt email the patron receives when an item is checked out will have the initial due date assigned by Alma, not the actual due date.
2.  The new due dates being assigned by the API as part of a checkout & renew can conflict with the library open hours in Alma.  For example, a loan could be due back on a holiday when the library is closed.

## Testing
The testing directory contains example NCIP messages and a simple PowerShell testing harness.  This was used for testing that NCIP requests were getting prepared for Alma correctly.  To test the NCIP responses were correctly formatted for the DCB system CMU coordinated  testing sessions with MeL staff.
