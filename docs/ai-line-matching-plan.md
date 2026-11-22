# AI line matching and paid plans (planned, not built)

Status: **idea, waiting on month-end budget** (noted 2026-11-22). Nothing here is implemented.

## Intention
After the code importer reads a workbook, send only the lines it could not categorise to an AI model, show the suggestions on the normal review screen, and let the user confirm. The user sees a short "Checking your lines…" step, then the review screen with suggestions filled in. Charge users for RandAhead so subscriptions cover the (small) API cost and more.

## Decisions so far
- **Narrow job.** The model only classifies unmatched account labels into the 13 categories. It never touches amounts.
- **Send labels only.** No amounts, no store or business names. Include the category list and the section heading each label sat under.
- **Suggestions, not auto-submit.** Suggestions are marked "suggested by AI" on the review screen and the user accepts them. Accepted ones are saved as remembered mappings (`import_mapping`) so each label is only ever asked once. Low-confidence results stay as "needs a category".
- **Why not fully automatic:** a wrong category silently changes forecasts and budgets. The importer cross-checks only catch some errors (Boardwalk FY2025 had mistakes in the sheet itself).
- **No MCP server.** The backend makes a plain API call to a model. MCP is for agents calling tools.
- **No local model.** The notebook is not suitable hardware.
- **Provider.** Hosted API only. DeepSeek would send data to servers in China, so it is the last choice for confidential books under POPIA. A Haiku-class Anthropic model is the likely pick. Check live pricing before committing.
- **Cost.** About 6 unmatched labels for Boardwalk and about 50 at most for a whole store, so roughly a couple of thousand tokens per upload, well under a cent. Controls: a monthly AI-call cap per business, fall back to "needs a category" at the cap, and the existing `RateLimits.cs` on the endpoint.

## Do first (cheaper than AI, may shrink the AI step)
- Widen the keyword rules. On 2026-11-22 the rules gained more accounting terms and a "Group - Detail" rule, and Boardwalk still left 6 lines unmatched: Head Office Monthly Fee, Franchise Fee, DC Handeling Charge, Penalties / Interest SARS, Courier & Postage, and Electricity & Water.
- Match accounts by account code across stores. Within one workbook this is done; across stores it is not.

## Charging
- Monthly subscription **per store** in rand, with a small free trial or a free tier (one store, one upload a month). AI is included, not metered.
- Hosted checkout so card details never reach RandAhead. Look at Paystack and PayFast for rand recurring billing. Check whether Stripe supports a South African business account.
- Build: a `subscription` table in `database/init/*.sql` (business, plan, status, period end, provider customer id); a webhook endpoint with signature verification; entitlement checks on upload, forecast and AI endpoints; a billing page that links to the provider portal.

## Needed before charging anyone
- Real accounts (sign-up, password reset, one login per business). Today there is one shared demo login.
- Public hosting with HTTPS, backups and uptime. Today it only runs on `127.0.0.1` on the notebook.
- Privacy policy, terms and POPIA compliance. Check VAT and tax on the income with an accountant.
- Multi-store support (one business, many stores). A separate session task exists for this.

## Related
- `RandAhead-Monetisation-Analysis.md` in the parent Grok Bot Project Analysis folder has the earlier monetisation thinking. It has not been reviewed against this plan.
- The multi-sheet importer lives in `backend/BudgetApi/Services/StatementImporter.Monthly.cs` (branch `store-workbook-import`).
