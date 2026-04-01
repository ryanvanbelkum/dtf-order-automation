# DTF Order Automation — Setup Guide

## What this app does
Automatically pulls unfulfilled orders from Shopify, looks up the right design
file, calculates the correct print size, and drops everything into your CADlink
hot folder — ready to RIP with no manual work.

---

## First-time setup (do this once)

### 1. Run the installer
- Open the `dtf_app` folder
- Double-click **"Install DTF App.bat"**
- A window will open and walk through setup automatically
- It takes about 2-3 minutes — don't close the window
- When it says **"All done!"** you're done

A **"DTF Order Automation"** shortcut will appear on your Desktop.

### 2. Configure the app
Open the app from your Desktop and go to the **Settings** tab. Fill in:

| Setting | What to enter |
|---|---|
| Shopify Store URL | e.g. `mystore.myshopify.com` |
| Shopify API Key | Your Admin API access token (see below) |
| Designs Folder | Full path to the folder with your PNG/JPG design files |
| Mapping File | Full path to `order_mapping.xlsx` |
| Hot Folder | Full path to the CADlink hot folder |

Click **Save Settings**.

---

## Getting your Shopify API Key
1. In Shopify Admin, go to **Settings → Apps and sales channels**
2. Click **Develop apps** → **Create an app**
3. Name it "DTF Automation"
4. Under **API credentials**, click **Configure Admin API scopes**
5. Enable: `read_orders`
6. Click **Save** then **Install app**
7. Copy the **Admin API access token** — paste it into the app Settings

---

## CADlink hot folder setup
1. In CADlink, go to **Queue → Properties → Hot Folders tab**
2. Check **Enable template hot folders**
3. Choose a folder location (this is what you paste into the app Settings)
4. Check **Delete file after processed by queue**
5. Click OK

---

## Daily use
The app runs in your **system tray** (bottom-right corner of taskbar).
- Double-click the tray icon to open the dashboard
- It runs automatically every hour by default
- Hit **Run Now** any time to process orders immediately
- The **Last Run** tab shows exactly what was processed
- Use **Pause Schedule** if you need to stop it temporarily

---

## Troubleshooting

**Orders are being skipped:**
Check the Last Run tab — it will say exactly why each order was skipped.
Usually it means the product name in the mapping spreadsheet doesn't match
Shopify exactly. Copy the product name directly from Shopify admin.

**"Could not connect to Shopify":**
Double-check your store URL and API key in Settings. The URL should not
include `https://` — just `mystore.myshopify.com`.

**Files not appearing in CADlink:**
Make sure the Hot Folder path in Settings matches exactly what's configured
in CADlink. Check that CADlink is running and the queue is active.
