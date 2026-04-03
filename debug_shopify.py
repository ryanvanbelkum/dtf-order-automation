"""
Shopify connection debugger — run this in Terminal on your Mac:
  python3 debug_shopify.py
"""

import urllib.request
import urllib.parse
import urllib.error
import json

print("─" * 50)
print("  Shopify Connection Debugger")
print("─" * 50)

store         = input("\nStore URL (e.g. olivestreetboutique.myshopify.com): ").strip().rstrip("/")
client_id     = input("Client ID: ").strip()
client_secret = input("Client Secret: ").strip()

print("\n[1] Requesting access token...")
url  = f"https://{store}/admin/oauth/access_token"
body = urllib.parse.urlencode({
    "grant_type":    "client_credentials",
    "client_id":     client_id,
    "client_secret": client_secret,
}).encode()

req = urllib.request.Request(url, data=body, headers={
    "Content-Type": "application/x-www-form-urlencoded"
})

token = None
try:
    with urllib.request.urlopen(req, timeout=10) as resp:
        raw  = resp.read()
        data = json.loads(raw)
        print(f"  ✓ HTTP {resp.status}")
        print(f"  Response: {json.dumps(data, indent=4)}")
        token = data.get("access_token")
except urllib.error.HTTPError as e:
    body_text = e.read().decode()
    print(f"  ✗ HTTP {e.code} {e.reason}")
    print(f"  Response body: {body_text}")
except urllib.error.URLError as e:
    print(f"  ✗ URL Error: {e.reason}")
except Exception as e:
    print(f"  ✗ Unexpected error: {e}")

if not token:
    print("\n✗ Could not get token — see error above.")
    exit(1)

print(f"\n  ✓ Token received: {token[:12]}...")
print(f"  Scopes: {data.get('scope')}")
print(f"  Expires in: {data.get('expires_in')}s")

print("\n[2] Fetching unfulfilled orders...")
url = f"https://{store}/admin/api/2024-01/orders.json?status=open&fulfillment_status=unfulfilled&limit=5"
req = urllib.request.Request(url, headers={
    "X-Shopify-Access-Token": token,
    "Content-Type": "application/json",
})
try:
    with urllib.request.urlopen(req, timeout=10) as resp:
        data   = json.loads(resp.read())
        orders = data.get("orders", [])
        print(f"  ✓ HTTP {resp.status} — {len(orders)} unfulfilled order(s) returned")
        for o in orders:
            print(f"    · {o.get('name')}  {o.get('created_at','')[:10]}")
except urllib.error.HTTPError as e:
    print(f"  ✗ HTTP {e.code} {e.reason}: {e.read().decode()}")
except Exception as e:
    print(f"  ✗ {e}")

print("\n[3] Fetching products...")
url = f"https://{store}/admin/api/2024-01/products.json?limit=5&fields=id,title"
req = urllib.request.Request(url, headers={
    "X-Shopify-Access-Token": token,
    "Content-Type": "application/json",
})
try:
    with urllib.request.urlopen(req, timeout=10) as resp:
        data     = json.loads(resp.read())
        products = data.get("products", [])
        print(f"  ✓ HTTP {resp.status} — {len(products)} product(s) returned (showing first 5)")
        for p in products:
            print(f"    · {p.get('title')}")
except urllib.error.HTTPError as e:
    print(f"  ✗ HTTP {e.code} {e.reason}: {e.read().decode()}")
except Exception as e:
    print(f"  ✗ {e}")

print("\n─" * 50)
print("  Done")
print("─" * 50)
