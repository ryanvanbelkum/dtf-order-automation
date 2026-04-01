import tkinter as tk
from tkinter import ttk, messagebox, scrolledtext
import threading
import time
import json
import os
import shutil
import sys
from datetime import datetime, timedelta
from PIL import Image, ImageDraw
import pystray

# ── paths ──────────────────────────────────────────────────────────────────
BASE_DIR = os.path.dirname(os.path.abspath(sys.argv[0]))
CONFIG_FILE = os.path.join(BASE_DIR, "dtf_config.json")
LOG_FILE    = os.path.join(BASE_DIR, "dtf_log.json")

DEFAULT_CONFIG = {
    "shopify_api_key":    "",
    "shopify_store_url":  "",
    "designs_folder":     "",
    "hot_folder":         "",
    "mapping_file":       "",
    "interval_hours":     1,
    "schedule_enabled":   True,
    "last_run":           None,
}

COLORS = {
    "bg":          "#1C1C1E",
    "surface":     "#2C2C2E",
    "surface2":    "#3A3A3C",
    "accent":      "#0A84FF",
    "accent_dark": "#0060CC",
    "success":     "#30D158",
    "warning":     "#FFD60A",
    "danger":      "#FF453A",
    "text":        "#FFFFFF",
    "text2":       "#AEAEB2",
    "text3":       "#636366",
    "border":      "#3A3A3C",
}

# ── config / log helpers ───────────────────────────────────────────────────
def load_config():
    if os.path.exists(CONFIG_FILE):
        with open(CONFIG_FILE) as f:
            cfg = json.load(f)
        for k, v in DEFAULT_CONFIG.items():
            cfg.setdefault(k, v)
        return cfg
    return dict(DEFAULT_CONFIG)

def save_config(cfg):
    with open(CONFIG_FILE, "w") as f:
        json.dump(cfg, f, indent=2)

def load_log():
    if os.path.exists(LOG_FILE):
        with open(LOG_FILE) as f:
            return json.load(f)
    return []

def save_log(log):
    with open(LOG_FILE, "w") as f:
        json.dump(log[-50:], f, indent=2)  # keep last 50 runs

# ── tray icon ──────────────────────────────────────────────────────────────
def make_tray_image():
    img = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
    d   = ImageDraw.Draw(img)
    d.ellipse([4, 4, 60, 60], fill="#0A84FF")
    d.rectangle([20, 18, 44, 46], fill="white")
    d.polygon([(26, 26), (26, 38), (38, 32)], fill="#0A84FF")
    return img

# ── main app ───────────────────────────────────────────────────────────────
class DTFApp:
    def __init__(self):
        self.config      = load_config()
        self.log         = load_log()
        self.running     = False
        self.next_run_dt = None
        self.tray        = None
        self.root        = None
        self._build_ui()
        self._start_scheduler()

    # ── UI construction ────────────────────────────────────────────────────
    def _build_ui(self):
        self.root = tk.Tk()
        self.root.title("DTF Order Automation")
        self.root.geometry("620x700")
        self.root.configure(bg=COLORS["bg"])
        self.root.resizable(False, False)
        self.root.protocol("WM_DELETE_WINDOW", self._hide_window)

        style = ttk.Style(self.root)
        style.theme_use("clam")
        style.configure("TNotebook",            background=COLORS["bg"],  borderwidth=0)
        style.configure("TNotebook.Tab",        background=COLORS["surface"], foreground=COLORS["text2"],
                        padding=[16, 8], font=("Segoe UI", 10))
        style.map("TNotebook.Tab",
                  background=[("selected", COLORS["surface2"])],
                  foreground=[("selected", COLORS["text"])])
        style.configure("TFrame",   background=COLORS["bg"])
        style.configure("TSeparator", background=COLORS["border"])

        # header
        hdr = tk.Frame(self.root, bg=COLORS["bg"], pady=20)
        hdr.pack(fill="x", padx=24)
        tk.Label(hdr, text="🖨  DTF Order Automation",
                 font=("Segoe UI", 18, "bold"),
                 bg=COLORS["bg"], fg=COLORS["text"]).pack(side="left")

        # status pill
        self.status_var = tk.StringVar(value="● Idle")
        self.status_lbl = tk.Label(hdr, textvariable=self.status_var,
                                   font=("Segoe UI", 10, "bold"),
                                   bg=COLORS["surface2"], fg=COLORS["text2"],
                                   padx=12, pady=4, relief="flat")
        self.status_lbl.pack(side="right")

        # notebook
        nb = ttk.Notebook(self.root)
        nb.pack(fill="both", expand=True, padx=16, pady=0)

        self._tab_dashboard(nb)
        self._tab_last_run(nb)
        self._tab_history(nb)
        self._tab_settings(nb)

        # tray
        self._setup_tray()
        self._refresh_dashboard()

    def _card(self, parent, title=None):
        outer = tk.Frame(parent, bg=COLORS["surface"], bd=0, pady=0)
        outer.pack(fill="x", padx=0, pady=6)
        inner = tk.Frame(outer, bg=COLORS["surface"], padx=18, pady=14)
        inner.pack(fill="x")
        if title:
            tk.Label(inner, text=title.upper(),
                     font=("Segoe UI", 8, "bold"),
                     bg=COLORS["surface"], fg=COLORS["text3"]).pack(anchor="w", pady=(0, 8))
        return inner

    # ── Dashboard tab ──────────────────────────────────────────────────────
    def _tab_dashboard(self, nb):
        frame = tk.Frame(nb, bg=COLORS["bg"])
        nb.add(frame, text="  Dashboard  ")

        scroll = tk.Frame(frame, bg=COLORS["bg"])
        scroll.pack(fill="both", expand=True, padx=16, pady=12)

        # next run card
        c = self._card(scroll, "Next Scheduled Run")
        self.next_run_var = tk.StringVar(value="—")
        self.countdown_var = tk.StringVar(value="")
        tk.Label(c, textvariable=self.next_run_var,
                 font=("Segoe UI", 22, "bold"),
                 bg=COLORS["surface"], fg=COLORS["text"]).pack(anchor="w")
        tk.Label(c, textvariable=self.countdown_var,
                 font=("Segoe UI", 11),
                 bg=COLORS["surface"], fg=COLORS["text2"]).pack(anchor="w", pady=(2, 0))

        # last run card
        c2 = self._card(scroll, "Last Run")
        self.last_run_var = tk.StringVar(value="Never")
        self.last_run_summary_var = tk.StringVar(value="")
        tk.Label(c2, textvariable=self.last_run_var,
                 font=("Segoe UI", 13, "bold"),
                 bg=COLORS["surface"], fg=COLORS["text"]).pack(anchor="w")
        tk.Label(c2, textvariable=self.last_run_summary_var,
                 font=("Segoe UI", 10),
                 bg=COLORS["surface"], fg=COLORS["text2"]).pack(anchor="w", pady=(2, 0))

        # schedule toggle card
        c3 = self._card(scroll, "Schedule")
        row = tk.Frame(c3, bg=COLORS["surface"])
        row.pack(fill="x")
        self.sched_enabled_var = tk.BooleanVar(value=self.config["schedule_enabled"])
        tk.Label(row, text="Run automatically every",
                 font=("Segoe UI", 11),
                 bg=COLORS["surface"], fg=COLORS["text"]).pack(side="left")
        self.interval_var = tk.StringVar(value=str(self.config["interval_hours"]))
        spin = tk.Spinbox(row, from_=1, to=24, width=3,
                          textvariable=self.interval_var,
                          font=("Segoe UI", 11),
                          bg=COLORS["surface2"], fg=COLORS["text"],
                          buttonbackground=COLORS["surface2"],
                          relief="flat", bd=4,
                          command=self._save_interval)
        spin.pack(side="left", padx=8)
        tk.Label(row, text="hour(s)",
                 font=("Segoe UI", 11),
                 bg=COLORS["surface"], fg=COLORS["text"]).pack(side="left")

        row2 = tk.Frame(c3, bg=COLORS["surface"])
        row2.pack(fill="x", pady=(10, 0))
        self.toggle_btn = tk.Button(row2,
                                    text="⏸  Pause Schedule" if self.config["schedule_enabled"] else "▶  Resume Schedule",
                                    font=("Segoe UI", 10, "bold"),
                                    bg=COLORS["surface2"], fg=COLORS["text2"],
                                    activebackground=COLORS["border"],
                                    relief="flat", bd=0, padx=14, pady=7,
                                    cursor="hand2",
                                    command=self._toggle_schedule)
        self.toggle_btn.pack(side="left")

        # run now button
        c4 = self._card(scroll)
        self.run_btn = tk.Button(c4,
                                 text="▶  Run Now",
                                 font=("Segoe UI", 13, "bold"),
                                 bg=COLORS["accent"], fg="white",
                                 activebackground=COLORS["accent_dark"],
                                 relief="flat", bd=0, padx=0, pady=12,
                                 cursor="hand2",
                                 command=self._run_now)
        self.run_btn.pack(fill="x")

    # ── Last Run tab ───────────────────────────────────────────────────────
    def _tab_last_run(self, nb):
        frame = tk.Frame(nb, bg=COLORS["bg"])
        nb.add(frame, text="  Last Run  ")

        self.last_run_detail = scrolledtext.ScrolledText(
            frame, font=("Consolas", 10),
            bg=COLORS["surface"], fg=COLORS["text"],
            insertbackground=COLORS["text"],
            relief="flat", bd=0, padx=16, pady=16,
            state="disabled"
        )
        self.last_run_detail.pack(fill="both", expand=True, padx=16, pady=12)
        self._refresh_last_run_tab()

    # ── History tab ────────────────────────────────────────────────────────
    def _tab_history(self, nb):
        frame = tk.Frame(nb, bg=COLORS["bg"])
        nb.add(frame, text="  History  ")

        cols = ("date", "orders", "skipped", "status")
        self.hist_tree = ttk.Treeview(frame, columns=cols, show="headings", height=18)
        style = ttk.Style()
        style.configure("Treeview",
                        background=COLORS["surface"],
                        foreground=COLORS["text"],
                        fieldbackground=COLORS["surface"],
                        rowheight=28,
                        font=("Segoe UI", 10))
        style.configure("Treeview.Heading",
                        background=COLORS["surface2"],
                        foreground=COLORS["text2"],
                        font=("Segoe UI", 9, "bold"))
        style.map("Treeview", background=[("selected", COLORS["accent"])])

        self.hist_tree.heading("date",    text="Date & Time")
        self.hist_tree.heading("orders",  text="Orders Processed")
        self.hist_tree.heading("skipped", text="Skipped")
        self.hist_tree.heading("status",  text="Status")
        self.hist_tree.column("date",    width=180)
        self.hist_tree.column("orders",  width=140, anchor="center")
        self.hist_tree.column("skipped", width=100, anchor="center")
        self.hist_tree.column("status",  width=120, anchor="center")

        sb = ttk.Scrollbar(frame, orient="vertical", command=self.hist_tree.yview)
        self.hist_tree.configure(yscrollcommand=sb.set)
        self.hist_tree.pack(side="left", fill="both", expand=True, padx=(16, 0), pady=12)
        sb.pack(side="right", fill="y", pady=12, padx=(0, 8))
        self._refresh_history()

    # ── Settings tab ──────────────────────────────────────────────────────
    def _tab_settings(self, nb):
        frame = tk.Frame(nb, bg=COLORS["bg"])
        nb.add(frame, text="  Settings  ")

        scroll = tk.Frame(frame, bg=COLORS["bg"])
        scroll.pack(fill="both", expand=True, padx=16, pady=12)

        fields = [
            ("Shopify Store URL",  "shopify_store_url",  "e.g. mystore.myshopify.com"),
            ("Shopify API Key",    "shopify_api_key",    "Admin API access token"),
            ("Designs Folder",     "designs_folder",     "Local folder containing design PNG/JPG files"),
            ("Mapping File",       "mapping_file",       "Path to order_mapping.xlsx"),
            ("Hot Folder",         "hot_folder",         "CADlink hot folder path"),
        ]

        self.setting_vars = {}
        for label, key, placeholder in fields:
            c = self._card(scroll, label)
            var = tk.StringVar(value=self.config.get(key, ""))
            self.setting_vars[key] = var
            entry = tk.Entry(c, textvariable=var,
                             font=("Segoe UI", 11),
                             bg=COLORS["surface2"], fg=COLORS["text"],
                             insertbackground=COLORS["text"],
                             relief="flat", bd=6)
            entry.pack(fill="x")
            tk.Label(c, text=placeholder,
                     font=("Segoe UI", 9),
                     bg=COLORS["surface"], fg=COLORS["text3"]).pack(anchor="w", pady=(4, 0))

        save_btn = tk.Button(scroll,
                             text="Save Settings",
                             font=("Segoe UI", 11, "bold"),
                             bg=COLORS["accent"], fg="white",
                             activebackground=COLORS["accent_dark"],
                             relief="flat", bd=0, padx=0, pady=10,
                             cursor="hand2",
                             command=self._save_settings)
        save_btn.pack(fill="x", pady=(8, 0))

    # ── Tray setup ─────────────────────────────────────────────────────────
    def _setup_tray(self):
        menu = pystray.Menu(
            pystray.MenuItem("Open",     lambda: self.root.after(0, self._show_window), default=True),
            pystray.MenuItem("Run Now",  lambda: self.root.after(0, self._run_now)),
            pystray.Menu.SEPARATOR,
            pystray.MenuItem("Quit",     lambda: self.root.after(0, self._quit)),
        )
        self.tray = pystray.Icon("DTF", make_tray_image(), "DTF Automation", menu)
        threading.Thread(target=self.tray.run, daemon=True).start()

    def _hide_window(self):
        self.root.withdraw()

    def _show_window(self):
        self.root.deiconify()
        self.root.lift()
        self.root.focus_force()

    def _quit(self):
        if self.tray:
            self.tray.stop()
        self.root.destroy()

    # ── Scheduler ─────────────────────────────────────────────────────────
    def _start_scheduler(self):
        self._schedule_next()
        self._tick()

    def _schedule_next(self):
        if self.config["schedule_enabled"]:
            hours = int(self.interval_var.get()) if hasattr(self, "interval_var") else self.config["interval_hours"]
            self.next_run_dt = datetime.now() + timedelta(hours=hours)
        else:
            self.next_run_dt = None

    def _tick(self):
        self._refresh_dashboard()
        if self.config["schedule_enabled"] and self.next_run_dt and not self.running:
            if datetime.now() >= self.next_run_dt:
                self._run_now()
        self.root.after(1000, self._tick)

    # ── Run logic ─────────────────────────────────────────────────────────
    def _run_now(self):
        if self.running:
            return
        self.running = True
        self.run_btn.config(state="disabled", text="⏳  Running…")
        self._set_status("● Running", COLORS["warning"])
        threading.Thread(target=self._do_run, daemon=True).start()

    def _do_run(self):
        result = run_automation(self.config)
        self.log.append(result)
        save_log(self.log)
        self.config["last_run"] = result["timestamp"]
        save_config(self.config)
        self.running = False
        self._schedule_next()
        self.root.after(0, self._on_run_complete, result)

    def _on_run_complete(self, result):
        self.run_btn.config(state="normal", text="▶  Run Now")
        status = "✓ Success" if result["status"] == "success" else "⚠ Completed with issues"
        color  = COLORS["success"] if result["status"] == "success" else COLORS["warning"]
        self._set_status(f"● {status}", color)
        self._refresh_dashboard()
        self._refresh_last_run_tab()
        self._refresh_history()
        self.root.after(5000, lambda: self._set_status("● Idle", COLORS["text2"]))

    # ── UI helpers ─────────────────────────────────────────────────────────
    def _set_status(self, text, color):
        self.status_var.set(text)
        self.status_lbl.config(fg=color)

    def _refresh_dashboard(self):
        # last run
        if self.config["last_run"]:
            dt = datetime.fromisoformat(self.config["last_run"])
            self.last_run_var.set(dt.strftime("%b %d, %Y at %I:%M %p"))
            if self.log:
                r = self.log[-1]
                self.last_run_summary_var.set(
                    f"{r.get('orders_processed', 0)} orders processed  ·  "
                    f"{r.get('files_queued', 0)} files queued  ·  "
                    f"{r.get('skipped', 0)} skipped"
                )
        else:
            self.last_run_var.set("Never")
            self.last_run_summary_var.set("No runs yet")

        # next run
        if self.next_run_dt and self.config["schedule_enabled"]:
            self.next_run_var.set(self.next_run_dt.strftime("%I:%M %p"))
            delta = self.next_run_dt - datetime.now()
            total = int(delta.total_seconds())
            if total > 0:
                h, rem = divmod(total, 3600)
                m, s   = divmod(rem, 60)
                if h > 0:
                    self.countdown_var.set(f"in {h}h {m}m {s}s")
                else:
                    self.countdown_var.set(f"in {m}m {s}s")
            else:
                self.countdown_var.set("Running now…")
        else:
            self.next_run_var.set("Paused")
            self.countdown_var.set("Schedule is disabled")

    def _refresh_last_run_tab(self):
        self.last_run_detail.config(state="normal")
        self.last_run_detail.delete("1.0", "end")
        if not self.log:
            self.last_run_detail.insert("end", "No runs yet.\n")
        else:
            r = self.log[-1]
            lines = [
                f"Run completed: {r.get('timestamp', '—')}",
                f"Status:        {r.get('status', '—').upper()}",
                "",
                f"Orders processed:  {r.get('orders_processed', 0)}",
                f"Files queued:      {r.get('files_queued', 0)}",
                f"Skipped:           {r.get('skipped', 0)}",
                "",
                "── Orders ──────────────────────────────────────",
            ]
            for o in r.get("order_details", []):
                icon = "✓" if o["status"] == "ok" else "⚠"
                lines.append(f"  {icon}  {o['order_id']}  ·  {o['product']}  ({o['size']})  →  {o.get('file', '—')}")

            if r.get("skipped_details"):
                lines += ["", "── Skipped ─────────────────────────────────────"]
                for s in r["skipped_details"]:
                    lines.append(f"  ⚠  {s['order_id']}  ·  {s['reason']}")

            if r.get("hot_folder_files"):
                lines += ["", "── Files dropped into hot folder ───────────────"]
                for f in r["hot_folder_files"]:
                    lines.append(f"  →  {f}")

            self.last_run_detail.insert("end", "\n".join(lines))
        self.last_run_detail.config(state="disabled")

    def _refresh_history(self):
        for row in self.hist_tree.get_children():
            self.hist_tree.delete(row)
        for r in reversed(self.log):
            dt  = datetime.fromisoformat(r["timestamp"]).strftime("%b %d  %I:%M %p")
            tag = "ok" if r["status"] == "success" else "warn"
            self.hist_tree.insert("", "end",
                                  values=(dt,
                                          r.get("orders_processed", 0),
                                          r.get("skipped", 0),
                                          "✓ Success" if r["status"] == "success" else "⚠ Issues"),
                                  tags=(tag,))
        self.hist_tree.tag_configure("ok",   foreground=COLORS["success"])
        self.hist_tree.tag_configure("warn", foreground=COLORS["warning"])

    def _toggle_schedule(self):
        self.config["schedule_enabled"] = not self.config["schedule_enabled"]
        save_config(self.config)
        if self.config["schedule_enabled"]:
            self._schedule_next()
            self.toggle_btn.config(text="⏸  Pause Schedule")
        else:
            self.next_run_dt = None
            self.toggle_btn.config(text="▶  Resume Schedule")
        self._refresh_dashboard()

    def _save_interval(self):
        try:
            hours = int(self.interval_var.get())
            self.config["interval_hours"] = hours
            save_config(self.config)
            self._schedule_next()
        except ValueError:
            pass

    def _save_settings(self):
        for key, var in self.setting_vars.items():
            self.config[key] = var.get().strip()
        save_config(self.config)
        messagebox.showinfo("Saved", "Settings saved successfully.")

    def run(self):
        self.root.mainloop()


# ── Automation engine ──────────────────────────────────────────────────────
def run_automation(config):
    """
    Core logic: pull orders → look up mapping → calculate sizing →
    write .jhdr → copy design file to hot folder.

    Returns a result dict for logging.
    """
    import openpyxl
    from PIL import Image as PILImage

    timestamp = datetime.now().isoformat()
    result = {
        "timestamp":        timestamp,
        "status":           "success",
        "orders_processed": 0,
        "files_queued":     0,
        "skipped":          0,
        "order_details":    [],
        "skipped_details":  [],
        "hot_folder_files": [],
    }

    CHILD_SIZES = {"YXS", "YS", "YM", "YL"}

    # ── load mapping spreadsheet ──
    mapping = {}
    try:
        wb = openpyxl.load_workbook(config["mapping_file"], read_only=True)
        ws = wb.active
        for row in ws.iter_rows(min_row=3, values_only=True):
            if row[0] and row[1]:
                mapping[str(row[0]).strip()] = str(row[1]).strip()
        wb.close()
    except Exception as e:
        result["status"] = "error"
        result["skipped_details"].append({"order_id": "—", "reason": f"Could not load mapping file: {e}"})
        return result

    # ── fetch orders from Shopify ──
    orders = fetch_shopify_orders(config)
    if orders is None:
        result["status"] = "error"
        result["skipped_details"].append({"order_id": "—", "reason": "Could not connect to Shopify"})
        return result

    for order in orders:
        order_id    = order.get("name", order.get("id", "?"))
        line_items  = order.get("line_items", [])

        for item in line_items:
            product_name = item.get("name", "").strip()
            size         = _extract_size(item)

            # look up design file
            design_file = mapping.get(product_name)
            if not design_file:
                result["skipped"] += 1
                result["skipped_details"].append({
                    "order_id": order_id,
                    "reason":   f"'{product_name}' not found in mapping spreadsheet",
                })
                result["order_details"].append({
                    "order_id": order_id, "product": product_name,
                    "size": size, "status": "skipped", "file": None,
                })
                continue

            design_path = os.path.join(config["designs_folder"], design_file)
            if not os.path.exists(design_path):
                result["skipped"] += 1
                result["skipped_details"].append({
                    "order_id": order_id,
                    "reason":   f"Design file not found: {design_file}",
                })
                result["order_details"].append({
                    "order_id": order_id, "product": product_name,
                    "size": size, "status": "skipped", "file": design_file,
                })
                continue

            # calculate dimensions
            try:
                width_in, height_in = _calculate_size(design_path, size, CHILD_SIZES)
            except Exception as e:
                result["skipped"] += 1
                result["skipped_details"].append({"order_id": order_id, "reason": str(e)})
                continue

            # write .jhdr then copy design to hot folder
            try:
                base_name = f"{order_id}_{product_name}_{size}".replace(" ", "_").replace("/", "-")
                jhdr_name = base_name + ".jhdr"
                img_name  = base_name + os.path.splitext(design_file)[1]

                jhdr_path = os.path.join(config["hot_folder"], jhdr_name)
                img_dst   = os.path.join(config["hot_folder"], img_name)

                _write_jhdr(jhdr_path, width_in, height_in)
                time.sleep(0.1)  # jhdr must arrive before image
                shutil.copy2(design_path, img_dst)

                result["files_queued"]    += 1
                result["orders_processed"] += 1
                result["hot_folder_files"].extend([jhdr_name, img_name])
                result["order_details"].append({
                    "order_id": order_id, "product": product_name,
                    "size": size, "status": "ok", "file": img_name,
                })
            except Exception as e:
                result["skipped"] += 1
                result["skipped_details"].append({"order_id": order_id, "reason": str(e)})

    if result["skipped"] > 0 and result["orders_processed"] == 0:
        result["status"] = "error"
    elif result["skipped"] > 0:
        result["status"] = "partial"

    return result


def _extract_size(line_item):
    for prop in line_item.get("properties", []):
        if prop.get("name", "").lower() in ("size", "Size"):
            return str(prop["value"]).strip().upper()
    for opt in line_item.get("variant_title", "").split(" / "):
        return opt.strip().upper()
    return "?"


def _calculate_size(design_path, size, child_sizes):
    from PIL import Image as PILImage
    with PILImage.open(design_path) as img:
        w_px, h_px = img.size

    is_child    = size.upper() in child_sizes
    is_landscape = w_px > h_px

    if is_landscape:
        width_in  = 11.0 if is_child else 12.0
        height_in = round(width_in * (h_px / w_px), 3)
    else:
        width_in  = 10.0 if is_child else 11.0
        height_in = width_in  # square

    return width_in, height_in


def _write_jhdr(path, width_in, height_in):
    pts_w = round(width_in  * 72, 2)
    pts_h = round(height_in * 72, 2)
    xml = f"""<?xml version="1.0" encoding="UTF-8" standalone="no"?>
<JHDR>
  <Size sizetype="0" width="{pts_w}" height="{pts_h}" />
</JHDR>
"""
    with open(path, "w") as f:
        f.write(xml)


def fetch_shopify_orders(config):
    """Fetch unfulfilled orders from Shopify via Admin API."""
    import urllib.request
    import urllib.error

    store = config.get("shopify_store_url", "").strip().rstrip("/")
    key   = config.get("shopify_api_key", "").strip()

    if not store or not key:
        return []

    url = f"https://{store}/admin/api/2024-01/orders.json?status=open&fulfillment_status=unfulfilled&limit=250"
    req = urllib.request.Request(url, headers={
        "X-Shopify-Access-Token": key,
        "Content-Type": "application/json",
    })
    try:
        with urllib.request.urlopen(req, timeout=15) as resp:
            data = json.loads(resp.read())
            return data.get("orders", [])
    except Exception:
        return None


# ── entry point ────────────────────────────────────────────────────────────
if __name__ == "__main__":
    app = DTFApp()
    app.run()
