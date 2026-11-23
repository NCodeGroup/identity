# Terms of Service

- **Status:** Draft

These Key Terms and Order Form incorporate the Common Paper
[Cloud Service Agreement Standard Terms Version 3.0](https://commonpaper.com/standards/cloud-service-agreement/3.0),
free to use under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/), and take effect when they are published at
`https://ncodeid.com/legal/terms`
([ADR-1529](../decisions/1529-the-hosted-platforms-terms-dpa-and-privacy-policy-are-public-documents-built-on-common-papers-standard-terms.md)).

## Key Terms

| Term                     | Value                                                                                                                                                                                         |
| ------------------------ | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Provider                 | NCode Group                                                                                                                                                                                   |
| Customer                 | The organization or person that accepts these terms when it signs up.                                                                                                                         |
| Effective Date           | The date the Customer accepts these terms.                                                                                                                                                    |
| Governing Law            | The laws of the State of Colorado, USA.                                                                                                                                                       |
| Chosen Courts            | The state and federal courts located in Denver, Colorado.                                                                                                                                     |
| General Cap Amount       | The Fees paid or payable by the Customer in the 12 months before the event giving rise to the claim, and $100 under the free developer plan.                                                  |
| Provider Covered Claims  | Any action, proceeding or claim that the Cloud Service, used as the Agreement permits, infringes or misappropriates a third party's intellectual property rights.                             |
| Customer Covered Claims  | Any action, proceeding or claim arising from the Customer's data or from the Customer's use of the Cloud Service in breach of the Agreement.                                                  |
| DPA                      | The [Data Processing Agreement](data-processing-agreement.md).                                                                                                                                |
| Notice Address           | `legal@ncodeid.com` for the Provider and the account owner's email address for the Customer.                                                                                                  |
| Prohibited Data          | Protected health information under HIPAA and payment card data under PCI DSS.                                                                                                                 |

## Order Form for self-serve plans

| Term                     | Value                                                                                                                                                                  |
| ------------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Order Date               | The date the Customer selects a plan.                                                                                                                                  |
| Cloud Service            | The NCode Identity hosted platform: its OpenID Connect and OAuth 2.0 endpoints under `ncodeid.net`, its management console and its management API.                     |
| Subscription Period      | One month, renewing monthly.                                                                                                                                           |
| Non-Renewal Notice Date  | Any time before the next renewal, by cancelling in the management console.                                                                                             |
| Fees                     | The price of the selected plan on `https://ncodeid.com/pricing`, $0 for the developer plan, in U.S. Dollars.                                                           |
| Payment                  | Charged to the payment method on file monthly in advance.                                                                                                              |
| Technical Support        | `support@ncodeid.com`, best effort on the developer plan and a first response within one business day on paid plans.                                                   |

## Service Level Agreement

The Service Level Agreement applies to paid plans only. The developer plan has no SLA.

- Availability: the protocol endpoints are available 99.9% of each calendar month, measured by the platform's external
  probes and excluding announced maintenance of at most four hours a month.
- Service credits: 10% of the month's Fees when availability is below 99.9%, and 25% when it is below 99.0%, claimed
  within 30 days.
