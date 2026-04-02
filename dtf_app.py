import customtkinter as ctk
import tkinter as tk
from tkinter import ttk, messagebox, filedialog
import threading
import queue
import time
import json
import os
import shutil
import sys
from datetime import datetime, timedelta
from PIL import Image, ImageDraw
import pystray

ctk.set_appearance_mode("dark")
ctk.set_default_color_theme("blue")

# ── paths ──────────────────────────────────────────────────────────────────
BASE_DIR     = os.path.dirname(os.path.abspath(sys.argv[0]))
CONFIG_FILE  = os.path.join(BASE_DIR, "dtf_config.json")
LOG_FILE     = os.path.join(BASE_DIR, "dtf_log.json")
MAPPING_FILE = os.path.join(BASE_DIR, "dtf_mapping.json")

DEFAULT_CONFIG = {
    "shopify_store_url":   "",
    "shopify_client_id":   "",
    "shopify_client_secret": "",
    "shopify_token":       "",
    "shopify_token_expiry": "",
    "designs_folder":      "",
    "hot_folder":          "",
    "interval_hours":      1,
    "schedule_enabled":    True,
    "last_run":            None,
}

# ── config / log / mapping helpers ────────────────────────────────────────
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
        json.dump(log[-50:], f, indent=2)

def load_mapping():
    if os.path.exists(MAPPING_FILE):
        with open(MAPPING_FILE) as f:
            return json.load(f)
    return {}

def save_mapping(mapping):
    with open(MAPPING_FILE, "w") as f:
        json.dump(mapping, f, indent=2)

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
        self._stop_event = threading.Event()
        self._log_queue  = queue.Queue()
        self._mappings   = load_mapping()
        self._mapping_rows = {}
        self._build_ui()
        self._start_scheduler()
        self._drain_log()

    # ── UI construction ────────────────────────────────────────────────────
    def _build_ui(self):
        self.root = ctk.CTk()
        self.root.title("DTF Order Automation")
        self.root.geometry("720x780")
        self.root.resizable(False, False)
        self.root.protocol("WM_DELETE_WINDOW", self._hide_window)

        # header
        header = ctk.CTkFrame(self.root, fg_color="transparent")
        header.pack(fill="x", padx=24, pady=(20, 4))

        ctk.CTkLabel(header, text="DTF Order Automation",
                     font=ctk.CTkFont(size=20, weight="bold")).pack(side="left")

        self.status_var = tk.StringVar(value="● Idle")
        self.status_lbl = ctk.CTkLabel(
            header,
            textvariable=self.status_var,
            font=ctk.CTkFont(size=11, weight="bold"),
            fg_color=("#e0e0e0", "#2c2c2e"),
            corner_radius=8,
            padx=12, pady=4,
            text_color="gray60",
        )
        self.status_lbl.pack(side="right")

        # tabs
        self.tabview = ctk.CTkTabview(self.root, corner_radius=12)
        self.tabview.pack(fill="both", expand=True, padx=16, pady=(4, 16))

        for name in ("Dashboard", "Mapping", "Last Run", "History", "Settings"):
            self.tabview.add(name)

        self._tab_dashboard()
        self._tab_mapping()
        self._tab_last_run()
        self._tab_history()
        self._tab_settings()

        self._setup_tray()
        self._refresh_dashboard()

    def _card(self, parent, title=None):
        outer = ctk.CTkFrame(parent, corner_radius=12)
        outer.pack(fill="x", pady=5)
        inner = ctk.CTkFrame(outer, fg_color="transparent")
        inner.pack(fill="x", padx=18, pady=14)
        if title:
            ctk.CTkLabel(inner, text=title.upper(),
                         font=ctk.CTkFont(size=9, weight="bold"),
                         text_color="gray50").pack(anchor="w", pady=(0, 8))
        return inner

    # ── Dashboard tab ──────────────────────────────────────────────────────
    def _tab_dashboard(self):
        scroll = ctk.CTkScrollableFrame(
            self.tabview.tab("Dashboard"), fg_color="transparent"
        )
        scroll.pack(fill="both", expand=True)

        c = self._card(scroll, "Next Scheduled Run")
        self.next_run_var  = tk.StringVar(value="—")
        self.countdown_var = tk.StringVar(value="")
        ctk.CTkLabel(c, textvariable=self.next_run_var,
                     font=ctk.CTkFont(size=28, weight="bold")).pack(anchor="w")
        ctk.CTkLabel(c, textvariable=self.countdown_var,
                     font=ctk.CTkFont(size=12),
                     text_color="gray55").pack(anchor="w", pady=(2, 0))

        c2 = self._card(scroll, "Last Run")
        self.last_run_var         = tk.StringVar(value="Never")
        self.last_run_summary_var = tk.StringVar(value="")
        ctk.CTkLabel(c2, textvariable=self.last_run_var,
                     font=ctk.CTkFont(size=14, weight="bold")).pack(anchor="w")
        ctk.CTkLabel(c2, textvariable=self.last_run_summary_var,
                     font=ctk.CTkFont(size=11),
                     text_color="gray55").pack(anchor="w", pady=(2, 0))

        c3 = self._card(scroll, "Schedule")
        interval_row = ctk.CTkFrame(c3, fg_color="transparent")
        interval_row.pack(fill="x")
        ctk.CTkLabel(interval_row, text="Run automatically every",
                     font=ctk.CTkFont(size=12)).pack(side="left")
        self.interval_var = tk.IntVar(value=self.config["interval_hours"])
        spin = ctk.CTkFrame(interval_row, fg_color="transparent")
        spin.pack(side="left", padx=10)
        ctk.CTkButton(spin, text="−", width=30, height=30, corner_radius=6,
                      command=lambda: self._adjust_interval(-1)).pack(side="left")
        ctk.CTkLabel(spin, textvariable=self.interval_var,
                     font=ctk.CTkFont(size=13, weight="bold"), width=34).pack(side="left")
        ctk.CTkButton(spin, text="+", width=30, height=30, corner_radius=6,
                      command=lambda: self._adjust_interval(1)).pack(side="left")
        ctk.CTkLabel(interval_row, text="hour(s)",
                     font=ctk.CTkFont(size=12)).pack(side="left")

        switch_row = ctk.CTkFrame(c3, fg_color="transparent")
        switch_row.pack(fill="x", pady=(12, 0))
        self.sched_switch = ctk.CTkSwitch(
            switch_row, text="Schedule enabled",
            font=ctk.CTkFont(size=12),
            command=self._toggle_schedule,
        )
        if self.config["schedule_enabled"]:
            self.sched_switch.select()
        else:
            self.sched_switch.deselect()
        self.sched_switch.pack(side="left")

        c4 = self._card(scroll)
        self.run_btn = ctk.CTkButton(
            c4, text="▶  Run Now",
            font=ctk.CTkFont(size=14, weight="bold"),
            height=50, corner_radius=10,
            command=self._run_btn_clicked,
        )
        self.run_btn.pack(fill="x")

    # ── Mapping tab ────────────────────────────────────────────────────────
    def _tab_mapping(self):
        tab = self.tabview.tab("Mapping")

        # top bar
        top = ctk.CTkFrame(tab, fg_color="transparent")
        top.pack(fill="x", padx=8, pady=(8, 0))

        self.sync_btn = ctk.CTkButton(
            top, text="↻  Sync Products from Shopify", width=230,
            command=self._sync_products,
        )
        self.sync_btn.pack(side="left")

        self.sync_status_var = tk.StringVar(value="")
        ctk.CTkLabel(top, textvariable=self.sync_status_var,
                     font=ctk.CTkFont(size=11),
                     text_color="gray55").pack(side="left", padx=12)

        ctk.CTkButton(top, text="Save Mappings", width=130,
                      command=self._save_mappings).pack(side="right")

        # divider
        ctk.CTkFrame(tab, height=1, fg_color=("gray75", "gray30")).pack(
            fill="x", padx=8, pady=8
        )

        # column headers
        hdr = ctk.CTkFrame(tab, fg_color="transparent")
        hdr.pack(fill="x", padx=16)
        ctk.CTkLabel(hdr, text="PRODUCT",
                     font=ctk.CTkFont(size=9, weight="bold"),
                     text_color="gray50", anchor="w").pack(side="left", expand=True, fill="x")
        ctk.CTkLabel(hdr, text="DESIGN FILE",
                     font=ctk.CTkFont(size=9, weight="bold"),
                     text_color="gray50", width=200, anchor="w").pack(side="left")
        ctk.CTkFrame(hdr, width=90, fg_color="transparent").pack(side="left")  # spacer for Browse btn

        # scrollable list
        self.mapping_scroll = ctk.CTkScrollableFrame(tab, fg_color="transparent")
        self.mapping_scroll.pack(fill="both", expand=True, padx=8, pady=4)

        self.mapping_placeholder = ctk.CTkLabel(
            self.mapping_scroll,
            text='Click "Sync Products from Shopify" to load your product list.',
            font=ctk.CTkFont(size=12),
            text_color="gray50",
        )
        self.mapping_placeholder.pack(pady=48)

    def _sync_products(self):
        if not self.config.get("shopify_store_url") or not self.config.get("shopify_client_id") or not self.config.get("shopify_client_secret"):
            messagebox.showwarning(
                "Missing settings",
                "Enter your Shopify Store URL and API Key in the Settings tab first."
            )
            return
        self.sync_btn.configure(state="disabled", text="⏳  Syncing…")
        self.sync_status_var.set("")
        threading.Thread(target=self._do_sync_products, daemon=True).start()

    def _do_sync_products(self):
        products = fetch_shopify_products(self.config)
        self.root.after(0, self._on_products_synced, products)

    def _on_products_synced(self, products):
        self.sync_btn.configure(state="normal", text="↻  Sync Products from Shopify")

        if products is None:
            self.sync_status_var.set("✗ Could not connect to Shopify")
            return
        if not products:
            self.sync_status_var.set("No products found")
            return

        self.sync_status_var.set(f"{len(products)} product(s) loaded")

        # rebuild list
        for w in self.mapping_scroll.winfo_children():
            w.destroy()
        self._mapping_rows = {}

        for product in products:
            self._add_mapping_row(product["title"])

    def _add_mapping_row(self, product_name):
        current_file = self._mappings.get(product_name, "")
        is_mapped    = bool(current_file)

        row = ctk.CTkFrame(self.mapping_scroll,
                           fg_color=("gray90", "gray17"), corner_radius=8)
        row.pack(fill="x", pady=2)

        inner = ctk.CTkFrame(row, fg_color="transparent")
        inner.pack(fill="x", padx=12, pady=8)

        ctk.CTkLabel(inner, text=product_name,
                     font=ctk.CTkFont(size=12),
                     anchor="w").pack(side="left", expand=True, fill="x")

        file_var = tk.StringVar(value=current_file if is_mapped else "Not mapped")
        file_lbl = ctk.CTkLabel(
            inner, textvariable=file_var,
            font=ctk.CTkFont(size=11),
            text_color=("gray40", "gray60") if not is_mapped else ("gray10", "gray90"),
            width=200, anchor="w",
        )
        file_lbl.pack(side="left", padx=(0, 10))

        ctk.CTkButton(
            inner, text="Browse…", width=85, height=28, corner_radius=6,
            command=self._make_browse_cmd(product_name, file_var, file_lbl),
        ).pack(side="right")

        self._mapping_rows[product_name] = {"file_var": file_var, "file_lbl": file_lbl}

    def _make_browse_cmd(self, product_name, file_var, file_lbl):
        def cmd():
            start_dir = self.config.get("designs_folder") or os.path.expanduser("~")
            path = filedialog.askopenfilename(
                title=f"Select design for: {product_name}",
                initialdir=start_dir,
                filetypes=[
                    ("Image files", "*.png *.jpg *.jpeg *.PNG *.JPG *.JPEG"),
                    ("All files", "*.*"),
                ],
            )
            if path:
                filename = os.path.basename(path)
                self._mappings[product_name] = filename
                file_var.set(filename)
                file_lbl.configure(text_color=("gray10", "gray90"))
        return cmd

    def _save_mappings(self):
        save_mapping(self._mappings)
        mapped = sum(1 for v in self._mappings.values() if v)
        messagebox.showinfo("Saved", f"Mappings saved — {mapped} product(s) mapped.")

    # ── Last Run tab ───────────────────────────────────────────────────────
    def _tab_last_run(self):
        self.last_run_detail = ctk.CTkTextbox(
            self.tabview.tab("Last Run"),
            font=ctk.CTkFont(family="Consolas", size=11),
            wrap="none",
            state="disabled",
            corner_radius=10,
        )
        self.last_run_detail.pack(fill="both", expand=True, padx=4, pady=4)
        self._refresh_last_run_tab()

    # ── History tab ────────────────────────────────────────────────────────
    def _tab_history(self):
        tab = self.tabview.tab("History")

        style = ttk.Style()
        style.theme_use("clam")
        style.configure("Dark.Treeview",
                        background="#2b2b2b", foreground="#ffffff",
                        fieldbackground="#2b2b2b", rowheight=30,
                        font=("Segoe UI", 10), borderwidth=0)
        style.configure("Dark.Treeview.Heading",
                        background="#3a3a3a", foreground="#aaaaaa",
                        font=("Segoe UI", 9, "bold"), relief="flat")
        style.map("Dark.Treeview", background=[("selected", "#1f6aa5")])

        cols = ("date", "orders", "skipped", "status")
        self.hist_tree = ttk.Treeview(tab, columns=cols, show="headings",
                                      style="Dark.Treeview", height=18)
        self.hist_tree.heading("date",    text="Date & Time")
        self.hist_tree.heading("orders",  text="Orders Processed")
        self.hist_tree.heading("skipped", text="Skipped")
        self.hist_tree.heading("status",  text="Status")
        self.hist_tree.column("date",    width=180)
        self.hist_tree.column("orders",  width=140, anchor="center")
        self.hist_tree.column("skipped", width=100, anchor="center")
        self.hist_tree.column("status",  width=120, anchor="center")

        sb = ttk.Scrollbar(tab, orient="vertical", command=self.hist_tree.yview)
        self.hist_tree.configure(yscrollcommand=sb.set)
        self.hist_tree.pack(side="left", fill="both", expand=True, padx=(4, 0), pady=4)
        sb.pack(side="right", fill="y", pady=4, padx=(0, 4))
        self._refresh_history()

    # ── Settings tab ──────────────────────────────────────────────────────
    def _tab_settings(self):
        scroll = ctk.CTkScrollableFrame(
            self.tabview.tab("Settings"), fg_color="transparent"
        )
        scroll.pack(fill="both", expand=True)

        fields = [
            ("Shopify Store URL",   "shopify_store_url",     "e.g. mystore.myshopify.com"),
            ("Shopify Client ID",   "shopify_client_id",     "Client ID from Dev Dashboard → Settings"),
            ("Shopify Client Secret", "shopify_client_secret", "Client Secret from Dev Dashboard → Settings"),
            ("Designs Folder",      "designs_folder",        "Folder containing design PNG/JPG files"),
            ("Hot Folder",          "hot_folder",            "CADlink hot folder path"),
        ]

        self.setting_vars = {}
        for label, key, hint in fields:
            c = self._card(scroll, label)
            var = tk.StringVar(value=self.config.get(key, ""))
            self.setting_vars[key] = var
            ctk.CTkEntry(c, textvariable=var,
                         font=ctk.CTkFont(size=12),
                         height=36, corner_radius=8).pack(fill="x")
            ctk.CTkLabel(c, text=hint, font=ctk.CTkFont(size=10),
                         text_color="gray50").pack(anchor="w", pady=(4, 0))

        c_btn = self._card(scroll)
        ctk.CTkButton(c_btn, text="Save Settings",
                      font=ctk.CTkFont(size=13, weight="bold"),
                      height=44, corner_radius=10,
                      command=self._save_settings).pack(fill="x")

    # ── Tray ───────────────────────────────────────────────────────────────
    def _setup_tray(self):
        menu = pystray.Menu(
            pystray.MenuItem("Open",    lambda: self.root.after(0, self._show_window), default=True),
            pystray.MenuItem("Run Now", lambda: self.root.after(0, self._run_now)),
            pystray.Menu.SEPARATOR,
            pystray.MenuItem("Quit",    lambda: self.root.after(0, self._quit)),
        )
        self.tray = pystray.Icon("DTF", make_tray_image(), "DTF Automation", menu)
        threading.Thread(target=self.tray.run, daemon=True).start()

    def _hide_window(self):  self.root.withdraw()
    def _show_window(self):
        self.root.deiconify(); self.root.lift(); self.root.focus_force()
    def _quit(self):
        if self.tray: self.tray.stop()
        self.root.destroy()

    # ── Scheduler ─────────────────────────────────────────────────────────
    def _start_scheduler(self):
        self._schedule_next()
        self._tick()

    def _schedule_next(self):
        if self.config["schedule_enabled"]:
            hours = self.interval_var.get() if hasattr(self, "interval_var") else self.config["interval_hours"]
            self.next_run_dt = datetime.now() + timedelta(hours=hours)
        else:
            self.next_run_dt = None

    def _tick(self):
        self._refresh_dashboard()
        if self.config["schedule_enabled"] and self.next_run_dt and not self.running:
            if datetime.now() >= self.next_run_dt:
                self._run_now()
        self.root.after(1000, self._tick)

    # ── Live log drain ─────────────────────────────────────────────────────
    def _drain_log(self):
        try:
            while True:
                msg = self._log_queue.get_nowait()
                self.last_run_detail.configure(state="normal")
                self.last_run_detail.insert("end", msg + "\n")
                self.last_run_detail.see("end")
                self.last_run_detail.configure(state="disabled")
        except queue.Empty:
            pass
        self.root.after(200, self._drain_log)

    # ── Run / Stop ─────────────────────────────────────────────────────────
    def _run_btn_clicked(self):
        if self.running:
            self._stop_run()
        else:
            self._run_now()

    def _run_now(self):
        if self.running:
            return
        self._stop_event.clear()
        self.running = True

        self.tabview.set("Last Run")
        self.last_run_detail.configure(state="normal")
        self.last_run_detail.delete("1.0", "end")
        self.last_run_detail.configure(state="disabled")

        self.run_btn.configure(
            text="■  Stop",
            fg_color=("#c0392b", "#C0392B"),
            hover_color=("#922b21", "#922B21"),
        )
        self._set_status("● Running", "#FFD60A")
        threading.Thread(target=self._do_run, daemon=True).start()

    def _stop_run(self):
        self._stop_event.set()
        self.run_btn.configure(state="disabled", text="⏳  Stopping…")

    def _do_run(self):
        def log_cb(msg):
            self._log_queue.put(msg)

        result = None
        try:
            result = run_automation(self.config, log_cb=log_cb, stop_event=self._stop_event)
            self.log.append(result)
            save_log(self.log)
            self.config["last_run"] = result["timestamp"]
            save_config(self.config)
        except Exception as e:
            log_cb(f"\n✗ Unexpected error: {e}")
            result = {
                "timestamp":        datetime.now().isoformat(),
                "status":           "error",
                "orders_processed": 0,
                "files_queued":     0,
                "skipped":          0,
                "order_details":    [],
                "skipped_details":  [{"order_id": "—", "reason": f"Unexpected error: {e}"}],
                "hot_folder_files": [],
            }
        finally:
            self.running = False
            self._schedule_next()
            self.root.after(0, self._on_run_complete, result)

    def _on_run_complete(self, result):
        self.run_btn.configure(
            state="normal", text="▶  Run Now",
            fg_color=["#3a7ebf", "#1f6aa5"],
            hover_color=["#325882", "#144870"],
        )
        if result["status"] == "success":
            status_text, status_color = "● Success", "#30D158"
        elif result["status"] == "stopped":
            status_text, status_color = "● Stopped", "#FF453A"
        else:
            status_text, status_color = "● Completed with issues", "#FFD60A"

        self._set_status(status_text, status_color)
        self._refresh_dashboard()
        self._refresh_last_run_tab()
        self._refresh_history()
        self.root.after(5000, lambda: self._set_status("● Idle", "gray60"))

    # ── UI refresh helpers ─────────────────────────────────────────────────
    def _set_status(self, text, color):
        self.status_var.set(text)
        self.status_lbl.configure(text_color=color)

    def _refresh_dashboard(self):
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

        if self.next_run_dt and self.config["schedule_enabled"]:
            self.next_run_var.set(self.next_run_dt.strftime("%I:%M %p"))
            delta = self.next_run_dt - datetime.now()
            total = int(delta.total_seconds())
            if total > 0:
                h, rem = divmod(total, 3600)
                m, s   = divmod(rem, 60)
                self.countdown_var.set(f"in {h}h {m}m {s}s" if h > 0 else f"in {m}m {s}s")
            else:
                self.countdown_var.set("Running now…")
        else:
            self.next_run_var.set("Paused")
            self.countdown_var.set("Schedule is disabled")

    def _refresh_last_run_tab(self):
        self.last_run_detail.configure(state="normal")
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
        self.last_run_detail.configure(state="disabled")

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
        self.hist_tree.tag_configure("ok",   foreground="#30D158")
        self.hist_tree.tag_configure("warn", foreground="#FFD60A")

    def _toggle_schedule(self):
        self.config["schedule_enabled"] = bool(self.sched_switch.get())
        save_config(self.config)
        if self.config["schedule_enabled"]:
            self._schedule_next()
        else:
            self.next_run_dt = None
        self._refresh_dashboard()

    def _adjust_interval(self, delta):
        val = max(1, min(24, self.interval_var.get() + delta))
        self.interval_var.set(val)
        self.config["interval_hours"] = val
        save_config(self.config)
        self._schedule_next()

    def _save_settings(self):
        for key, var in self.setting_vars.items():
            self.config[key] = var.get().strip()
        save_config(self.config)
        messagebox.showinfo("Saved", "Settings saved successfully.")

    def run(self):
        self.root.mainloop()


# ── Automation engine ──────────────────────────────────────────────────────
def run_automation(config, log_cb=None, stop_event=None):
    def log(msg):
        if log_cb: log_cb(msg)

    def stopped():
        return stop_event is not None and stop_event.is_set()

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

    # ── load mapping ──
    log("Loading product mappings…")
    mapping = load_mapping()
    if not mapping:
        log("  ⚠ No mappings configured — go to the Mapping tab to set up your products")
    else:
        log(f"  ✓ {len(mapping)} product(s) mapped")

    if stopped():
        result["status"] = "stopped"
        log("\n⚠ Stopped by user.")
        return result

    # ── fetch orders ──
    log("\nFetching orders from Shopify…")
    orders = fetch_shopify_orders(config)
    if orders is None:
        result["status"] = "error"
        result["skipped_details"].append({"order_id": "—", "reason": "Could not connect to Shopify"})
        log("  ✗ Could not connect to Shopify — check URL and API key in Settings")
        return result
    log(f"  ✓ Found {len(orders)} unfulfilled order(s)")

    if not orders:
        log("\nNothing to do.")
        return result

    log("")

    for order in orders:
        if stopped():
            log("\n⚠ Stopped by user.")
            result["status"] = "stopped"
            break

        order_id   = order.get("name", order.get("id", "?"))
        line_items = order.get("line_items", [])
        log(f"Order {order_id}  ({len(line_items)} line item(s))")

        for item in line_items:
            if stopped():
                break

            product_name = item.get("name", "").strip()
            size         = _extract_size(item)

            design_file = mapping.get(product_name)
            if not design_file:
                log(f"  ⚠ {product_name} ({size}) — not in mapping, skipped")
                result["skipped"] += 1
                result["skipped_details"].append({"order_id": order_id, "reason": f"'{product_name}' not in mapping"})
                result["order_details"].append({"order_id": order_id, "product": product_name, "size": size, "status": "skipped", "file": None})
                continue

            design_path = os.path.join(config["designs_folder"], design_file)
            if not os.path.exists(design_path):
                log(f"  ⚠ {product_name} ({size}) — design file not found: {design_file}")
                result["skipped"] += 1
                result["skipped_details"].append({"order_id": order_id, "reason": f"Design file not found: {design_file}"})
                result["order_details"].append({"order_id": order_id, "product": product_name, "size": size, "status": "skipped", "file": design_file})
                continue

            try:
                width_in, height_in = _calculate_size(design_path, size, CHILD_SIZES)
            except Exception as e:
                log(f"  ⚠ {product_name} ({size}) — sizing error: {e}")
                result["skipped"] += 1
                result["skipped_details"].append({"order_id": order_id, "reason": str(e)})
                continue

            try:
                base_name = f"{order_id}_{product_name}_{size}".replace(" ", "_").replace("/", "-")
                jhdr_name = base_name + ".jhdr"
                img_name  = base_name + os.path.splitext(design_file)[1]
                jhdr_path = os.path.join(config["hot_folder"], jhdr_name)
                img_dst   = os.path.join(config["hot_folder"], img_name)

                _write_jhdr(jhdr_path, width_in, height_in)
                time.sleep(0.1)
                shutil.copy2(design_path, img_dst)

                log(f"  ✓ {product_name} ({size})  →  {img_name}  [{width_in}\" × {height_in}\"]")
                result["files_queued"]     += 1
                result["orders_processed"] += 1
                result["hot_folder_files"].extend([jhdr_name, img_name])
                result["order_details"].append({"order_id": order_id, "product": product_name, "size": size, "status": "ok", "file": img_name})
            except Exception as e:
                log(f"  ✗ {product_name} ({size}) — hot folder error: {e}")
                result["skipped"] += 1
                result["skipped_details"].append({"order_id": order_id, "reason": str(e)})

    if result["status"] != "stopped":
        if result["skipped"] > 0 and result["orders_processed"] == 0:
            result["status"] = "error"
        elif result["skipped"] > 0:
            result["status"] = "partial"

    log(
        f"\n{'─' * 48}\n"
        f"Done: {result['orders_processed']} processed · "
        f"{result['files_queued']} queued · "
        f"{result['skipped']} skipped"
    )
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
    is_child     = size.upper() in child_sizes
    is_landscape = w_px > h_px
    if is_landscape:
        width_in  = 11.0 if is_child else 12.0
        height_in = round(width_in * (h_px / w_px), 3)
    else:
        width_in  = 10.0 if is_child else 11.0
        height_in = width_in
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


def get_shopify_token(config):
    """
    Obtain an access token via the client credentials grant.
    Returns a cached token if still valid, otherwise requests a new one
    and saves it to config. Token is valid for 24 hours; we refresh at 23.
    """
    import urllib.request

    store         = config.get("shopify_store_url", "").strip().rstrip("/")
    client_id     = config.get("shopify_client_id", "").strip()
    client_secret = config.get("shopify_client_secret", "").strip()

    if not store or not client_id or not client_secret:
        return None

    # return cached token if still fresh
    expiry = config.get("shopify_token_expiry", "")
    token  = config.get("shopify_token", "")
    if token and expiry:
        try:
            if datetime.fromisoformat(expiry) > datetime.now():
                return token
        except ValueError:
            pass

    # request a new token
    url  = f"https://{store}/admin/oauth/access_token"
    body = json.dumps({
        "grant_type":    "client_credentials",
        "client_id":     client_id,
        "client_secret": client_secret,
    }).encode()
    req = urllib.request.Request(url, data=body,
                                 headers={"Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=8) as resp:
            data  = json.loads(resp.read())
            token = data.get("access_token")
            if token:
                config["shopify_token"]        = token
                config["shopify_token_expiry"] = (datetime.now() + timedelta(hours=23)).isoformat()
                save_config(config)
            return token
    except Exception:
        return None


def fetch_shopify_orders(config):
    import urllib.request
    store = config.get("shopify_store_url", "").strip().rstrip("/")
    if not store:
        return []
    token = get_shopify_token(config)
    if not token:
        return None
    url = f"https://{store}/admin/api/2024-01/orders.json?status=open&fulfillment_status=unfulfilled&limit=250"
    req = urllib.request.Request(url, headers={"X-Shopify-Access-Token": token, "Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req, timeout=8) as resp:
            return json.loads(resp.read()).get("orders", [])
    except Exception:
        return None


def fetch_shopify_products(config):
    import urllib.request
    store = config.get("shopify_store_url", "").strip().rstrip("/")
    if not store:
        return []
    token = get_shopify_token(config)
    if not token:
        return None

    products = []
    url = f"https://{store}/admin/api/2024-01/products.json?limit=250&fields=id,title"
    while url:
        req = urllib.request.Request(url, headers={"X-Shopify-Access-Token": token, "Content-Type": "application/json"})
        try:
            with urllib.request.urlopen(req, timeout=8) as resp:
                products.extend(json.loads(resp.read()).get("products", []))
                link = resp.headers.get("Link", "")
                url  = None
                if 'rel="next"' in link:
                    for part in link.split(","):
                        if 'rel="next"' in part:
                            url = part.split(";")[0].strip().strip("<>")
                            break
        except Exception:
            return None
    return products


# ── entry point ────────────────────────────────────────────────────────────
if __name__ == "__main__":
    app = DTFApp()
    app.run()
