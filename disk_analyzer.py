#!/usr/bin/env python3
"""
Disk Space Analyzer - A lightweight GUI tool to identify large folders
and free up disk space on Windows.

Requirements: Python 3.6+ (uses only standard library)
Run: python disk_analyzer.py
"""

import tkinter as tk
from tkinter import ttk, filedialog, messagebox
import os
import shutil
import threading
import subprocess
import sys
from pathlib import Path


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

def get_dir_size(path):
    """Recursively calculate total size of a directory, skipping inaccessible entries."""
    total = 0
    try:
        with os.scandir(path) as it:
            for entry in it:
                try:
                    if entry.is_file(follow_symlinks=False):
                        total += entry.stat(follow_symlinks=False).st_size
                    elif entry.is_dir(follow_symlinks=False):
                        total += get_dir_size(entry.path)
                except (OSError, PermissionError):
                    pass
    except (OSError, PermissionError):
        pass
    return total


def format_size(size_bytes):
    """Convert bytes to a human-readable string."""
    for unit in ('B', 'KB', 'MB', 'GB', 'TB'):
        if size_bytes < 1024.0:
            return f"{size_bytes:.1f} {unit}"
        size_bytes /= 1024.0
    return f"{size_bytes:.1f} PB"


def get_windows_drives():
    """Return a list of available drive letters on Windows."""
    drives = []
    try:
        import ctypes
        bitmask = ctypes.windll.kernel32.GetLogicalDrives()
        for letter in 'ABCDEFGHIJKLMNOPQRSTUVWXYZ':
            if bitmask & 1:
                drives.append(f"{letter}:\\")
            bitmask >>= 1
    except Exception:
        pass
    return drives


FILE_TYPE_MAP = {
    '.exe': 'App',       '.msi': 'Installer',  '.dll': 'Library',
    '.zip': 'Archive',   '.rar': 'Archive',     '.7z': 'Archive',
    '.tar': 'Archive',   '.gz': 'Archive',      '.bz2': 'Archive',
    '.mp4': 'Video',     '.mkv': 'Video',       '.avi': 'Video',
    '.mov': 'Video',     '.wmv': 'Video',
    '.mp3': 'Audio',     '.wav': 'Audio',       '.flac': 'Audio',
    '.aac': 'Audio',
    '.jpg': 'Image',     '.jpeg': 'Image',      '.png': 'Image',
    '.gif': 'Image',     '.bmp': 'Image',       '.tiff': 'Image',
    '.pdf': 'Document',  '.doc': 'Document',    '.docx': 'Document',
    '.xls': 'Document',  '.xlsx': 'Document',   '.pptx': 'Document',
    '.iso': 'Disk Img',  '.vmdk': 'Disk Img',   '.vhd': 'Disk Img',
    '.pst': 'Email',     '.ost': 'Email',
    '.log': 'Log',       '.tmp': 'Temp',        '.bak': 'Backup',
    '.cab': 'Cabinet',
}


def get_file_type(filename):
    ext = os.path.splitext(filename)[1].lower()
    return FILE_TYPE_MAP.get(ext, 'File')


# ---------------------------------------------------------------------------
# Main Application
# ---------------------------------------------------------------------------

class DiskAnalyzerApp:

    BAR_WIDTH = 18   # number of block chars in the usage bar

    def __init__(self, root):
        self.root = root
        self.root.title("Disk Space Analyzer")
        self.root.geometry("960x680")
        self.root.minsize(720, 520)

        # Internal state
        self.current_path = None
        self.path_history = []
        self.scan_thread = None
        self.scanning = False
        self._cancel = False
        self.items_data = {}        # tree-item-id -> dict
        self._sort_reverse = {}     # column -> bool

        self._setup_styles()
        self._build_ui()
        self._populate_drives()

    # ------------------------------------------------------------------
    # UI Construction
    # ------------------------------------------------------------------

    def _setup_styles(self):
        style = ttk.Style()
        style.theme_use('clam')
        style.configure('Title.TLabel',  font=('Segoe UI', 14, 'bold'))
        style.configure('Header.TLabel', font=('Segoe UI', 9,  'bold'))
        style.configure('TButton',       font=('Segoe UI', 9))
        style.configure('Accent.TButton', font=('Segoe UI', 9, 'bold'),
                        foreground='white', background='#0078D4')
        style.map('Accent.TButton',
                  background=[('active', '#005a9e'), ('pressed', '#004578')])
        style.configure('Treeview',         font=('Segoe UI', 9), rowheight=24)
        style.configure('Treeview.Heading', font=('Segoe UI', 9, 'bold'))
        style.map('Treeview',
                  background=[('selected', '#0078D4')],
                  foreground=[('selected', 'white')])

    def _build_ui(self):
        outer = ttk.Frame(self.root, padding=10)
        outer.pack(fill=tk.BOTH, expand=True)

        # ── Title ──────────────────────────────────────────────────────
        ttk.Label(outer, text="Disk Space Analyzer", style='Title.TLabel').pack(
            anchor=tk.W, pady=(0, 8))

        # ── Location Row ──────────────────────────────────────────────
        loc = ttk.LabelFrame(outer, text="Scan Location", padding=(8, 4))
        loc.pack(fill=tk.X, pady=(0, 6))

        ttk.Label(loc, text="Drive:").pack(side=tk.LEFT)
        self.drive_var = tk.StringVar()
        self.drive_combo = ttk.Combobox(loc, textvariable=self.drive_var,
                                        width=9, state='readonly')
        self.drive_combo.pack(side=tk.LEFT, padx=(4, 10))
        self.drive_combo.bind('<<ComboboxSelected>>', self._on_drive_selected)

        ttk.Label(loc, text="Path:").pack(side=tk.LEFT)
        self.path_var = tk.StringVar()
        path_entry = ttk.Entry(loc, textvariable=self.path_var)
        path_entry.pack(side=tk.LEFT, padx=(4, 4), fill=tk.X, expand=True)

        ttk.Button(loc, text="Browse...", command=self._browse).pack(side=tk.LEFT, padx=2)
        self.scan_btn = ttk.Button(loc, text="  Scan  ", command=self._start_scan,
                                   style='Accent.TButton')
        self.scan_btn.pack(side=tk.LEFT, padx=(8, 2))
        self.cancel_btn = ttk.Button(loc, text="Cancel", command=self._cancel_scan,
                                     state=tk.DISABLED)
        self.cancel_btn.pack(side=tk.LEFT, padx=2)

        # ── Disk Usage Bar ────────────────────────────────────────────
        usage_lf = ttk.LabelFrame(outer, text="Disk Usage", padding=(8, 4))
        usage_lf.pack(fill=tk.X, pady=(0, 6))

        self.usage_canvas = tk.Canvas(usage_lf, height=30, bg='#d0d0d0',
                                      highlightthickness=0, relief='flat')
        self.usage_canvas.pack(fill=tk.X, pady=(2, 0))
        self.usage_canvas.bind('<Configure>', self._redraw_usage)

        # ── Navigation Bar ────────────────────────────────────────────
        nav = ttk.Frame(outer)
        nav.pack(fill=tk.X, pady=(0, 4))

        self.back_btn = ttk.Button(nav, text="\u2190 Back",
                                   command=self._go_back, state=tk.DISABLED)
        self.back_btn.pack(side=tk.LEFT, padx=(0, 8))
        self.nav_label = ttk.Label(nav, text="", style='Header.TLabel')
        self.nav_label.pack(side=tk.LEFT)

        # ── Results Tree ──────────────────────────────────────────────
        tree_frame = ttk.Frame(outer)
        tree_frame.pack(fill=tk.BOTH, expand=True, pady=(0, 6))

        vsb = ttk.Scrollbar(tree_frame, orient=tk.VERTICAL)
        vsb.pack(side=tk.RIGHT, fill=tk.Y)
        hsb = ttk.Scrollbar(tree_frame, orient=tk.HORIZONTAL)
        hsb.pack(side=tk.BOTTOM, fill=tk.X)

        self.tree = ttk.Treeview(
            tree_frame,
            columns=('size', 'bar', 'items', 'type'),
            yscrollcommand=vsb.set,
            xscrollcommand=hsb.set,
            selectmode='extended',
        )
        vsb.config(command=self.tree.yview)
        hsb.config(command=self.tree.xview)
        self.tree.pack(fill=tk.BOTH, expand=True)

        self.tree.heading('#0',     text='Name',  command=lambda: self._sort('name'))
        self.tree.heading('size',   text='Size',  command=lambda: self._sort('size'))
        self.tree.heading('bar',    text='Usage (% of total shown)')
        self.tree.heading('items',  text='Items', command=lambda: self._sort('items'))
        self.tree.heading('type',   text='Type',  command=lambda: self._sort('type'))

        self.tree.column('#0',    width=340, minwidth=140)
        self.tree.column('size',  width=95,  anchor=tk.E,      minwidth=70)
        self.tree.column('bar',   width=200, anchor=tk.W,      minwidth=100)
        self.tree.column('items', width=70,  anchor=tk.CENTER, minwidth=50)
        self.tree.column('type',  width=90,  anchor=tk.CENTER, minwidth=60)

        # Row colour tags
        self.tree.tag_configure('folder',  foreground='#1a5276')
        self.tree.tag_configure('file',    foreground='#2c3e50')
        self.tree.tag_configure('giant',   background='#fde8e8')   # > 1 GB
        self.tree.tag_configure('large',   background='#fef3cd')   # > 100 MB
        self.tree.tag_configure('folder_giant', foreground='#1a5276', background='#fde8e8')
        self.tree.tag_configure('folder_large', foreground='#1a5276', background='#fef3cd')

        self.tree.bind('<Double-Button-1>', self._on_double_click)
        self.tree.bind('<Button-3>',        self._show_context_menu)

        # Context menu
        self.ctx = tk.Menu(self.root, tearoff=0)
        self.ctx.add_command(label="Open in Explorer",  command=self._open_explorer)
        self.ctx.add_command(label="Delete Selected",   command=self._delete_selected)
        self.ctx.add_separator()
        self.ctx.add_command(label="Copy Path",         command=self._copy_path)

        # ── Action Buttons ────────────────────────────────────────────
        act = ttk.Frame(outer)
        act.pack(fill=tk.X, pady=(0, 4))

        ttk.Button(act, text="Open in Explorer",  command=self._open_explorer).pack(side=tk.LEFT, padx=2)
        ttk.Button(act, text="Delete Selected",   command=self._delete_selected).pack(side=tk.LEFT, padx=2)
        ttk.Separator(act, orient=tk.VERTICAL).pack(side=tk.LEFT, fill=tk.Y, padx=10)
        ttk.Button(act, text="Clean Temp Files",  command=self._clean_temp).pack(side=tk.LEFT, padx=2)
        ttk.Button(act, text="Empty Recycle Bin", command=self._empty_recycle_bin).pack(side=tk.LEFT, padx=2)
        self.total_label = ttk.Label(act, text="")
        self.total_label.pack(side=tk.RIGHT, padx=4)

        # ── Status Bar ────────────────────────────────────────────────
        sb = ttk.Frame(outer)
        sb.pack(fill=tk.X)

        self.progress = ttk.Progressbar(sb, mode='indeterminate', length=140)
        self.progress.pack(side=tk.LEFT, padx=(0, 8))
        self.status_var = tk.StringVar(value="Ready – select a drive or folder and click Scan.")
        ttk.Label(sb, textvariable=self.status_var).pack(side=tk.LEFT)

    # ------------------------------------------------------------------
    # Drive / Path
    # ------------------------------------------------------------------

    def _populate_drives(self):
        if sys.platform == 'win32':
            drives = get_windows_drives()
        else:
            drives = [str(Path.home()), '/']

        self.drive_combo['values'] = drives
        if drives:
            default = 'C:\\' if 'C:\\' in drives else drives[0]
            self.drive_combo.set(default)
            self.path_var.set(default)
            self._update_disk_usage(default)

    def _on_drive_selected(self, _event=None):
        drive = self.drive_var.get()
        self.path_var.set(drive)
        self._update_disk_usage(drive)

    def _browse(self):
        folder = filedialog.askdirectory(initialdir=self.path_var.get() or '/')
        if folder:
            self.path_var.set(folder)

    # ------------------------------------------------------------------
    # Disk Usage Bar
    # ------------------------------------------------------------------

    def _update_disk_usage(self, path):
        try:
            self._usage = shutil.disk_usage(path)
        except Exception:
            self._usage = None
        self._redraw_usage()

    def _redraw_usage(self, _event=None):
        canvas = self.usage_canvas
        usage  = getattr(self, '_usage', None)
        w = canvas.winfo_width() or 900
        h = 30
        canvas.delete('all')

        if not usage:
            canvas.create_text(w // 2, h // 2, text="No disk selected",
                               fill='#666', font=('Segoe UI', 9))
            return

        used_pct = usage.used / usage.total if usage.total else 0
        used_px  = max(0, int(w * used_pct))

        # Colour ramp: green / amber / red
        if used_pct < 0.70:
            bar_color = '#27ae60'
        elif used_pct < 0.85:
            bar_color = '#e67e22'
        else:
            bar_color = '#c0392b'

        canvas.create_rectangle(0, 0, w, h, fill='#c8c8c8', outline='')
        if used_px:
            canvas.create_rectangle(0, 0, used_px, h, fill=bar_color, outline='')

        text = (
            f"Used: {format_size(usage.used)}  ({used_pct*100:.1f}%)   "
            f"Free: {format_size(usage.free)}   "
            f"Total: {format_size(usage.total)}"
        )
        txt_fill = 'white' if used_pct > 0.25 else '#333'
        canvas.create_text(w // 2, h // 2, text=text, fill=txt_fill,
                           font=('Segoe UI', 9, 'bold'), anchor=tk.CENTER)

    # ------------------------------------------------------------------
    # Scanning
    # ------------------------------------------------------------------

    def _start_scan(self):
        path = self.path_var.get().strip()
        if not path or not os.path.exists(path):
            messagebox.showwarning("Invalid Path",
                                   "Please enter or browse to a valid path.")
            return
        self.path_history.clear()
        self.back_btn.config(state=tk.DISABLED)
        self.current_path = path
        self._run_scan(path)

    def _run_scan(self, path):
        self.scanning = True
        self._cancel = False
        self.scan_btn.config(state=tk.DISABLED)
        self.cancel_btn.config(state=tk.NORMAL)
        self.progress.start(10)
        self.status_var.set(f"Scanning: {path}  …")
        self.nav_label.config(text=path)
        self.total_label.config(text="")

        # Refresh disk usage bar for the drive containing this path
        drive = Path(path).anchor or path
        self._update_disk_usage(drive)

        # Clear results
        for iid in self.tree.get_children():
            self.tree.delete(iid)
        self.items_data.clear()

        self.scan_thread = threading.Thread(
            target=self._scan_worker, args=(path,), daemon=True)
        self.scan_thread.start()

    def _scan_worker(self, path):
        results = []
        try:
            entries = list(os.scandir(path))
        except PermissionError:
            self._finish_scan([], path, error="Permission denied – try running as Administrator.")
            return
        except Exception as exc:
            self._finish_scan([], path, error=str(exc))
            return

        total = len(entries)
        for idx, entry in enumerate(entries):
            if self._cancel:
                self.root.after(0, self._on_cancelled)
                return

            try:
                is_dir = entry.is_dir(follow_symlinks=False)
                if is_dir:
                    size = get_dir_size(entry.path)
                    try:
                        sub_count = sum(1 for _ in os.scandir(entry.path))
                    except Exception:
                        sub_count = 0
                    ftype = 'Folder'
                else:
                    size = entry.stat(follow_symlinks=False).st_size
                    sub_count = 0
                    ftype = get_file_type(entry.name)

                results.append({
                    'path':      entry.path,
                    'name':      entry.name,
                    'size':      size,
                    'is_dir':    is_dir,
                    'sub_count': sub_count,
                    'type':      ftype,
                })
            except (OSError, PermissionError):
                continue

            if (idx + 1) % 5 == 0 or idx == total - 1:
                msg = f"Scanning ({idx+1}/{total}): {entry.name}"
                self.root.after(0, lambda m=msg: self.status_var.set(m))

        results.sort(key=lambda r: r['size'], reverse=True)
        self.root.after(0, lambda: self._finish_scan(results, path))

    def _finish_scan(self, results, path, error=None):
        self.scanning = False
        self.scan_btn.config(state=tk.NORMAL)
        self.cancel_btn.config(state=tk.DISABLED)
        self.progress.stop()

        if error:
            self.status_var.set(f"Error: {error}")
            messagebox.showerror("Scan Error", error)
            return

        if not results:
            self.status_var.set("No readable items found.")
            return

        total_size = sum(r['size'] for r in results)

        for r in results:
            pct   = r['size'] / total_size if total_size else 0
            filled = int(pct * self.BAR_WIDTH)
            bar   = '\u2588' * filled + '\u2591' * (self.BAR_WIDTH - filled)
            bar   += f'  {pct*100:.1f}%'

            is_dir = r['is_dir']
            if r['size'] >= 1024 ** 3:
                tag = 'folder_giant' if is_dir else 'giant'
            elif r['size'] >= 100 * 1024 ** 2:
                tag = 'folder_large' if is_dir else 'large'
            else:
                tag = 'folder' if is_dir else 'file'

            icon  = '[+] ' if is_dir else '    '
            label = icon + r['name']

            iid = self.tree.insert(
                '', tk.END,
                text=label,
                values=(
                    format_size(r['size']),
                    bar,
                    str(r['sub_count']) if is_dir else '-',
                    r['type'],
                ),
                tags=(tag,),
            )
            self.items_data[iid] = r

        self.status_var.set(
            f"Found {len(results)} items in: {path}")
        self.total_label.config(
            text=f"Shown total: {format_size(total_size)}")
        self.back_btn.config(
            state=tk.NORMAL if self.path_history else tk.DISABLED)

    def _on_cancelled(self):
        self.scanning = False
        self.scan_btn.config(state=tk.NORMAL)
        self.cancel_btn.config(state=tk.DISABLED)
        self.progress.stop()
        self.status_var.set("Scan cancelled.")

    def _cancel_scan(self):
        self._cancel = True

    # ------------------------------------------------------------------
    # Navigation
    # ------------------------------------------------------------------

    def _on_double_click(self, _event):
        iid = self.tree.focus()
        if iid and iid in self.items_data:
            data = self.items_data[iid]
            if data['is_dir']:
                self.path_history.append(self.current_path)
                self.current_path = data['path']
                self.path_var.set(data['path'])
                self._run_scan(data['path'])

    def _go_back(self):
        if self.path_history:
            prev = self.path_history.pop()
            self.current_path = prev
            self.path_var.set(prev)
            self._run_scan(prev)

    # ------------------------------------------------------------------
    # Sorting
    # ------------------------------------------------------------------

    def _sort(self, col):
        rev = self._sort_reverse.get(col, True)
        self._sort_reverse[col] = not rev

        key_fn = {
            'name':  lambda iid: self.items_data.get(iid, {}).get('name',      '').lower(),
            'size':  lambda iid: self.items_data.get(iid, {}).get('size',      0),
            'items': lambda iid: self.items_data.get(iid, {}).get('sub_count', 0),
            'type':  lambda iid: self.items_data.get(iid, {}).get('type',      ''),
        }.get(col, lambda iid: 0)

        rows = sorted(self.tree.get_children(''), key=key_fn, reverse=rev)
        for idx, iid in enumerate(rows):
            self.tree.move(iid, '', idx)

    # ------------------------------------------------------------------
    # Context Menu
    # ------------------------------------------------------------------

    def _show_context_menu(self, event):
        iid = self.tree.identify_row(event.y)
        if iid:
            if iid not in self.tree.selection():
                self.tree.selection_set(iid)
            self.ctx.post(event.x_root, event.y_root)

    def _selected_paths(self):
        return [
            self.items_data[iid]['path']
            for iid in self.tree.selection()
            if iid in self.items_data
        ]

    # ------------------------------------------------------------------
    # Actions
    # ------------------------------------------------------------------

    def _open_explorer(self):
        paths = self._selected_paths() or ([self.current_path] if self.current_path else [])
        if not paths:
            return
        path = paths[0]
        try:
            if sys.platform == 'win32':
                if os.path.isdir(path):
                    subprocess.Popen(['explorer', path])
                else:
                    subprocess.Popen(['explorer', '/select,', path])
            elif sys.platform == 'darwin':
                subprocess.Popen(['open', path if os.path.isdir(path) else os.path.dirname(path)])
            else:
                subprocess.Popen(['xdg-open', path if os.path.isdir(path) else os.path.dirname(path)])
        except Exception as exc:
            messagebox.showerror("Error", f"Could not open Explorer:\n{exc}")

    def _delete_selected(self):
        sel = [(iid, self.items_data[iid])
               for iid in self.tree.selection()
               if iid in self.items_data]
        if not sel:
            messagebox.showinfo("No Selection", "Please select items to delete.")
            return

        total_size = sum(d['size'] for _, d in sel)
        names = [d['name'] for _, d in sel]

        lines = '\n'.join(f"  \u2022 {n}" for n in names[:7])
        if len(names) > 7:
            lines += f"\n  \u2026 and {len(names) - 7} more"

        confirm = messagebox.askyesno(
            "Confirm Delete",
            f"Permanently delete {len(sel)} item(s)?\n\n"
            f"{lines}\n\n"
            f"Total size to free: {format_size(total_size)}\n\n"
            "This cannot be undone!",
            icon='warning',
        )
        if not confirm:
            return

        freed  = 0
        errors = []
        for iid, data in sel:
            try:
                if os.path.isdir(data['path']):
                    shutil.rmtree(data['path'])
                else:
                    os.remove(data['path'])
                freed += data['size']
            except Exception as exc:
                errors.append(f"{data['name']}: {exc}")

        if errors:
            messagebox.showerror(
                "Delete Errors",
                f"{len(errors)} item(s) could not be deleted:\n" + '\n'.join(errors[:5]),
            )

        if freed:
            self.status_var.set(
                f"Deleted {len(sel) - len(errors)} item(s). "
                f"Freed approx. {format_size(freed)}.")
            # Refresh the current directory
            self._run_scan(self.current_path or self.path_var.get())

    def _copy_path(self):
        paths = self._selected_paths()
        if paths:
            self.root.clipboard_clear()
            self.root.clipboard_append(paths[0])

    def _clean_temp(self):
        """Delete contents of Windows / system temp folders."""
        if sys.platform == 'win32':
            import tempfile
            candidates = [
                tempfile.gettempdir(),
                os.path.expandvars(r'%WINDIR%\Temp'),
                os.path.expandvars(r'%LOCALAPPDATA%\Temp'),
            ]
        else:
            candidates = ['/tmp']

        temp_dirs = list({d for d in candidates if os.path.isdir(d)})
        if not temp_dirs:
            messagebox.showinfo("Temp Files", "No temp directories found.")
            return

        size_info = []
        grand = 0
        for d in temp_dirs:
            s = get_dir_size(d)
            grand += s
            size_info.append(f"  {d}  ({format_size(s)})")

        confirm = messagebox.askyesno(
            "Clean Temp Files",
            "Delete contents of:\n" + '\n'.join(size_info) +
            f"\n\nTotal: {format_size(grand)}\n\nProceed?",
        )
        if not confirm:
            return

        freed  = 0
        errors = 0
        for d in temp_dirs:
            try:
                for entry in os.scandir(d):
                    try:
                        s = get_dir_size(entry.path) if entry.is_dir() else entry.stat().st_size
                        if entry.is_dir(follow_symlinks=False):
                            shutil.rmtree(entry.path, ignore_errors=True)
                        else:
                            os.remove(entry.path)
                        freed += s
                    except Exception:
                        errors += 1
            except Exception:
                errors += 1

        msg = f"Freed {format_size(freed)} from temp folders."
        if errors:
            msg += f"  ({errors} items skipped – in use or access denied)"
        self.status_var.set(msg)
        messagebox.showinfo("Temp Files Cleaned", msg)

    def _empty_recycle_bin(self):
        if sys.platform != 'win32':
            messagebox.showinfo("Not Supported",
                                "Empty Recycle Bin is only available on Windows.")
            return

        if not messagebox.askyesno(
            "Empty Recycle Bin",
            "Permanently delete all items in the Recycle Bin?\n\nThis cannot be undone!",
            icon='warning',
        ):
            return

        try:
            import ctypes
            # Flags: SHERB_NOCONFIRMATION=1 | SHERB_NOPROGRESSUI=2 | SHERB_NOSOUND=4
            rc = ctypes.windll.shell32.SHEmptyRecycleBinW(None, None, 7)
            # 0 = success; 0x80070057 = already empty (also fine)
            if rc in (0, -2147418113, 0x80070057):
                self.status_var.set("Recycle Bin emptied.")
                messagebox.showinfo("Done", "Recycle Bin has been emptied.")
            else:
                messagebox.showwarning("Result", f"SHEmptyRecycleBin returned 0x{rc & 0xFFFFFFFF:08X}")
        except Exception as exc:
            messagebox.showerror("Error", f"Could not empty Recycle Bin:\n{exc}")


# ---------------------------------------------------------------------------
# Entry Point
# ---------------------------------------------------------------------------

def main():
    root = tk.Tk()
    try:
        # High-DPI awareness on Windows
        if sys.platform == 'win32':
            import ctypes
            ctypes.windll.shcore.SetProcessDpiAwareness(1)
    except Exception:
        pass

    app = DiskAnalyzerApp(root)  # noqa: F841
    root.mainloop()


if __name__ == '__main__':
    main()
