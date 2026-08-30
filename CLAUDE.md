# DTF Order Automation — Project Rules

## 
The goal of this project is to automate business workflows for owners of apparel printing businesses. These owners use CadLink Direct To Fill version 11. Make sure all features consider this. 

## Non-Negotiable Features

These features must always work. Do not break, remove, or regress them. 

### Settings
- User can input Shopify API credentials (store URL, access token) and save them
- User can input folder settings (hotfolder path for CadLink output) and save them

### Product Sync & Mapping
- User can sync all products from their Shopify store
- User can match synced Shopify products to local design files
- Mapping is persisted across sessions

### Orders Tab
- User can fetch orders by selecting a start date and end date
- Fetched orders are split into two lists:
  - **Mapped orders**: products that have a matching design file
  - **Unmapped orders**: products with no design file mapping
- For unmapped orders, user can map them directly on the orders tab (inline mapping)
- Once an unmapped order is mapped, it moves to the mapped list immediately

### Order Actions
- User can select individual orders or use a "Select All" control
- User can click a button to copy the linked design files for selected orders to the configured CadLink hotfolder
- Clicking a row opens a modal with detailed information about the product or order

### History Tab
- A history tab shows a log of previous runs (fetches, copies, etc.)

### Dashboard Tab
- A dashboard tab displays statistics (e.g. orders processed, files copied)
- Dashboard includes a button to trigger an order fetch
