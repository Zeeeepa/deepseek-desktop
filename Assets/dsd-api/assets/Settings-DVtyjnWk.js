import { c as createLucideIcon, u as useTranslation, a8 as useTheme, k as useSettingsStore, j as jsxRuntimeExports, a9 as Sun, aa as Moon, B as Button, ab as Languages, ac as Power, Y as useToast, r as reactExports, $ as TriangleAlert, M as Dialog, ad as DialogTrigger, N as DialogContent, O as DialogHeader, Q as DialogTitle, T as DialogDescription, U as DialogFooter, F as FileText, a5 as Key, I as Check, L as Copy, K as Settings$1 } from "./index-CBGGrI33.js";
import { T as Tabs, a as TabsList, b as TabsTrigger, d as TabsContent } from "./tabs-CfApjdf_.js";
import { C as Card, a as CardHeader, b as CardTitle, c as CardContent, d as CardDescription } from "./card-B5CbN4-5.js";
import { L as Label, S as Switch, I as Input } from "./label-mNw1xWB1.js";
import { S as Select, b as SelectTrigger, d as SelectValue, e as SelectContent, f as SelectItem } from "./select--OXYUWzT.js";
import { S as Separator } from "./separator-B-E_5yxg.js";
import { G as Globe } from "./globe-DWpntor8.js";
import { D as Database } from "./database-B2KU6QrB.js";
import { D as Download } from "./download-pCPP42nr.js";
import { T as Trash2 } from "./trash-2-Csxc6eE1.js";
import { R as RotateCcw, A as Alert, a as AlertDescription } from "./alert-DJ-2nPYp.js";
import { E as EyeOff, a as Eye } from "./eye-B4JnEcH-.js";
import { R as RefreshCw } from "./refresh-cw-n0m7k033.js";
import { S as Shield } from "./shield-Dkbamauq.js";
import "./index-DZePc0M4.js";
import "./index-DOD0wY2v.js";
/**
 * @license lucide-react v0.454.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */
const Bell = createLucideIcon("Bell", [
  ["path", { d: "M6 8a6 6 0 0 1 12 0c0 7 3 9 3 9H3s3-2 3-9", key: "1qo2s2" }],
  ["path", { d: "M10.3 21a1.94 1.94 0 0 0 3.4 0", key: "qgo35s" }]
]);
/**
 * @license lucide-react v0.454.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */
const Lock = createLucideIcon("Lock", [
  ["rect", { width: "18", height: "11", x: "3", y: "11", rx: "2", ry: "2", key: "1w4ew1" }],
  ["path", { d: "M7 11V7a5 5 0 0 1 10 0v4", key: "fwvmzm" }]
]);
/**
 * @license lucide-react v0.454.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */
const Minimize2 = createLucideIcon("Minimize2", [
  ["polyline", { points: "4 14 10 14 10 20", key: "11kfnr" }],
  ["polyline", { points: "20 10 14 10 14 4", key: "rlmsce" }],
  ["line", { x1: "14", x2: "21", y1: "10", y2: "3", key: "o5lafz" }],
  ["line", { x1: "3", x2: "10", y1: "21", y2: "14", key: "1atl0r" }]
]);
/**
 * @license lucide-react v0.454.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */
const PanelLeft = createLucideIcon("PanelLeft", [
  ["rect", { width: "18", height: "18", x: "3", y: "3", rx: "2", key: "afitv7" }],
  ["path", { d: "M9 3v18", key: "fh3hqa" }]
]);
/**
 * @license lucide-react v0.454.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */
const Terminal = createLucideIcon("Terminal", [
  ["polyline", { points: "4 17 10 11 4 5", key: "akl6gq" }],
  ["line", { x1: "12", x2: "20", y1: "19", y2: "19", key: "q2wloq" }]
]);
/**
 * @license lucide-react v0.454.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */
const Upload = createLucideIcon("Upload", [
  ["path", { d: "M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4", key: "ih7n3h" }],
  ["polyline", { points: "17 8 12 3 7 8", key: "t8dd8p" }],
  ["line", { x1: "12", x2: "12", y1: "3", y2: "15", key: "widbto" }]
]);
function AppearanceSettings() {
  const { t } = useTranslation();
  const { theme, setTheme } = useTheme();
  const {
    sidebarCollapsed,
    setSidebarCollapsed,
    language,
    setLanguage
  } = useSettingsStore();
  return /* @__PURE__ */ jsxRuntimeExports.jsxs("div", { className: "space-y-6", children: [
    /* @__PURE__ */ jsxRuntimeExports.jsxs(Card, { children: [
      /* @__PURE__ */ jsxRuntimeExports.jsx(CardHeader, { children: /* @__PURE__ */ jsxRuntimeExports.jsxs(CardTitle, { className: "flex items-center gap-2", children: [
        /* @__PURE__ */ jsxRuntimeExports.jsx("div", { className: "h-7 w-7 rounded-lg bg-[var(--accent-primary)]/10 flex items-center justify-center", children: /* @__PURE__ */ jsxRuntimeExports.jsx(Sun, { className: "h-4 w-4 text-[var(--accent-primary)]" }) }),
        t("settings.theme")
      ] }) }),
      /* @__PURE__ */ jsxRuntimeExports.jsx(CardContent, { className: "space-y-6", children: /* @__PURE__ */ jsxRuntimeExports.jsxs("div", { className: "space-y-2", children: [
        /* @__PURE__ */ jsxRuntimeExports.jsx(Label, { children: t("settings.theme") }),
        /* @__PURE__ */ jsxRuntimeExports.jsx("div", { className: "flex gap-2", children: [
          { value: "light", labelKey: "settings.themeLight", icon: Sun },
          { value: "dark", labelKey: "settings.themeDark", icon: Moon }
        ].map(({ value, labelKey, icon: Icon }) => /* @__PURE__ */ jsxRuntimeExports.jsxs(
          Button,
          {
            variant: theme === value ? "default" : "outline",
            onClick: () => setTheme(value),
            className: "flex items-center gap-2",
            children: [
              /* @__PURE__ */ jsxRuntimeExports.jsx(Icon, { className: "h-4 w-4" }),
              t(labelKey)
            ]
          },
          value
        )) })
      ] }) })
    ] }),
    /* @__PURE__ */ jsxRuntimeExports.jsxs(Card, { children: [
      /* @__PURE__ */ jsxRuntimeExports.jsx(CardHeader, { children: /* @__PURE__ */ jsxRuntimeExports.jsxs(CardTitle, { className: "flex items-center gap-2", children: [
        /* @__PURE__ */ jsxRuntimeExports.jsx("div", { className: "h-7 w-7 rounded-lg bg-[var(--accent-primary)]/10 flex items-center justify-center", children: /* @__PURE__ */ jsxRuntimeExports.jsx(PanelLeft, { className: "h-4 w-4 text-[var(--accent-primary)]" }) }),
        t("settings.sidebar")
      ] }) }),
      /* @__PURE__ */ jsxRuntimeExports.jsx(CardContent, { className: "space-y-6", children: /* @__PURE__ */ jsxRuntimeExports.jsxs("div", { className: "flex items-center justify-between p-3 rounded-xl bg-[var(--glass-bg)] border border-[var(--glass-border)]", children: [
        /* @__PURE__ */ jsxRuntimeExports.jsxs("div", { className: "space-y-0.5", children: [
          /* @__PURE__ */ jsxRuntimeExports.jsx(Label, { htmlFor: "sidebar-collapsed", children: t("settings.sidebarCollapsed") }),
          /* @__PURE__ */ jsxRuntimeExports.jsx("p", { className: "text-sm text-muted-foreground", children: t("settings.sidebarCollapsedHelp") })
        ] }),
        /* @__PURE__ */ jsxRuntimeExports.jsx(
          Switch,
          {
            id: "sidebar-collapsed",
            checked: sidebarCollapsed,
            onCheckedChange: setSidebarCollapsed
          }
        )
      ] }) })
    ] })
  ] });
}
function GeneralSettings() {
  const { t } = useTranslation();
  const {
    autoStart,
    setAutoStart,
    autoStartProxy,
    setAutoStartProxy,
    minimizeToTray,
    setMinimizeToTray,
    closeBehavior,
    setCloseBehavior,
    enableNotifications,
    setEnableNotifications,
    oauthProxyMode,
    setOauthProxyMode
  } = useSettingsStore();
  return /* @__PURE__ */ jsxRuntimeExports.jsxs("div", { className: "space-y-6", children: [
    /* @__PURE__ */ jsxRuntimeExports.jsxs(Card, { children: [
      /* @__PURE__ */ jsxRuntimeExports.jsx(CardHeader, { className: "pb-3", children: /* @__PURE__ */ jsxRuntimeExports.jsxs(CardTitle, { className: "flex items-center gap-2", children: [
        /* @__PURE__ */ jsxRuntimeExports.jsx(Power, { className: "h-5 w-5" }),
        t("settings.autoStart")
      ] }) }),
      /* @__PURE__ */ jsxRuntimeExports.jsxs(CardContent, { className: "space-y-6", children: [
        /* @__PURE__ */ jsxRuntimeExports.jsxs("div", { className: "flex items-center justify-between", children: [
          /* @__PURE__ */ jsxRuntimeExports.jsxs("div", { className: "space-y-0.5", children: [
            /* @__PURE__ */ jsxRuntimeExports.jsx(Label, { htmlFor: "auto-start", children: t("settings.autoStart") }),
            /* @__PURE__ */ jsxRuntimeExports.jsx("p", { className: "text-sm text-muted-foreground", children: t("settings.autoStartHelp") })
          ] }),
          /* @__PURE__ */ jsxRuntimeExports.jsx(
            Switch,
            {
              id: "auto-start",
              checked: autoStart,
              onCheckedChange: setAutoStart
            }
          )
        ] }),
        /* @__PURE__ */ jsxRuntimeExports.jsx(Separator, {}),
        /* @__PURE__ */ jsxRuntimeExports.jsxs("div", { className: "flex items-center justify-between", children: [
          /* @__PURE__ */ jsxRuntimeExports.jsxs("div", { className: "space-y-0.5", children: [
            /* @__PURE__ */ jsxRuntimeExports.jsx(Label, { htmlFor: "auto-start-proxy", children: t("settings.autoStartProxy") }),
            /* @__PURE__ */ jsxRuntimeExports.jsx("p", { className: "text-sm text-muted-foreground", children: t("settings.autoStartProxyHelp") })
          ] }),
          /* @__PURE__ */ jsxRuntimeExports.jsx(
            Switch,
            {
              id: "auto-start-proxy",
              checked: autoStartProxy,
              onCheckedChange: setAutoStartProxy
            }
          )
        ] })
      ] })
    ] }),
    /* @__PURE__ */ jsxRuntimeExports.jsxs(Card, { children: [
      /* @__PURE__ */ jsxRuntimeExports.jsx(CardHeader, { className: "pb-3", children: /* @__PURE__ */ jsxRuntimeExports.jsxs(CardTitle, { className: "flex items-center gap-2", children: [
        /* @__PURE__ */ jsxRuntimeExports.jsx(Globe, { className: "h-5 w-5" }),
        t("settings.networkProxy")
      ] }) }),
      /* @__PURE__ */ jsxRuntimeExports.jsx(CardContent, { className: "space-y-6", children: /* @__PURE__ */ jsxRuntimeExports.jsxs("div", { className: "flex items-center justify-between", children: [
        /* @__PURE__ */ jsxRuntimeExports.jsxs("div", { className: "space-y-0.5", children: [
          /* @__PURE__ */ jsxRuntimeExports.jsx(Label, { children: t("settings.oauthProxyMode") }),
          /* @__PURE__ */ jsxRuntimeExports.jsx("p", { className: "text-sm text-muted-foreground", children: t("settings.oauthProxyModeHelp") })
        ] }),
        /* @__PURE__ */ jsxRuntimeExports.jsxs(
          Select,
          {
            value: oauthProxyMode,
            onValueChange: (value) => setOauthProxyMode(value),
            children: [
              /* @__PURE__ */ jsxRuntimeExports.jsx(SelectTrigger, { className: "w-[180px]", children: /* @__PURE__ */ jsxRuntimeExports.jsx(SelectValue, { placeholder: t("settings.oauthProxyMode") }) }),
              /* @__PURE__ */ jsxRuntimeExports.jsxs(SelectContent, { children: [
                /* @__PURE__ */ jsxRuntimeExports.jsx(SelectItem, { value: "system", children: t("settings.oauthProxySystem") }),
                /* @__PURE__ */ jsxRuntimeExports.jsx(SelectItem, { value: "none", children: t("settings.oauthProxyNone") })
              ] })
            ]
          }
        )
      ] }) })
    ] })
  ] });
}
function DataManagement() {
  const { t } = useTranslation();
  const {
    logLevel,
    setLogLevel,
    logRetentionDays,
    setLogRetentionDays,
    maxLogs,
    setMaxLogs,
    config,
    updateConfig
  } = useSettingsStore();
  const { toast } = useToast();
  const [isExporting, setIsExporting] = reactExports.useState(false);
  const [isImporting, setIsImporting] = reactExports.useState(false);
  const [isClearing, setIsClearing] = reactExports.useState(false);
  const [isResetting, setIsResetting] = reactExports.useState(false);
  const requestLogConfig = config?.requestLogConfig ?? {
    enabled: true,
    maxEntries: 200,
    includeBodies: false,
    maxBodyChars: 8e3,
    redactSensitiveData: true
  };
  const updateRequestLogConfig = async (updates) => {
    await updateConfig({
      requestLogConfig: {
        ...requestLogConfig,
        ...updates
      }
    });
  };
  const handleExportConfig = async () => {
    setIsExporting(true);
    try {
      const config2 = {
        version: "1.1.2",
        exportedAt: (/* @__PURE__ */ new Date()).toISOString(),
        settings: localStorage.getItem("dsd-desktop-api-settings")
      };
      const blob = new Blob([JSON.stringify(config2, null, 2)], { type: "application/json" });
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = `dsd-api-config-${(/* @__PURE__ */ new Date()).toISOString().split("T")[0]}.json`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
      toast({
        title: t("common.success"),
        description: t("settings.exportSuccess")
      });
    } catch {
      toast({
        title: t("common.error"),
        description: t("settings.exportFailed"),
        variant: "destructive"
      });
    } finally {
      setIsExporting(false);
    }
  };
  const handleImportConfig = async (event) => {
    const file = event.target.files?.[0];
    if (!file) return;
    setIsImporting(true);
    try {
      const text = await file.text();
      const config2 = JSON.parse(text);
      if (config2.settings) {
        localStorage.setItem("dsd-desktop-api-settings", config2.settings);
        toast({
          title: t("common.success"),
          description: t("settings.importSuccess")
        });
        setTimeout(() => {
          window.location.reload();
        }, 1500);
      }
    } catch {
      toast({
        title: t("common.error"),
        description: t("settings.importFailed"),
        variant: "destructive"
      });
    } finally {
      setIsImporting(false);
      event.target.value = "";
    }
  };
  const handleClearCache = async () => {
    setIsClearing(true);
    try {
      sessionStorage.clear();
      toast({
        title: t("common.success"),
        description: t("settings.cacheCleared")
      });
    } catch {
      toast({
        title: t("common.error"),
        description: t("settings.cacheClearFailed"),
        variant: "destructive"
      });
    } finally {
      setIsClearing(false);
    }
  };
  const handleResetApp = async () => {
    setIsResetting(true);
    try {
      localStorage.clear();
      sessionStorage.clear();
      if (window.electronAPI?.store?.clearAll) {
        await window.electronAPI.store.clearAll();
      }
      toast({
        title: t("common.success"),
        description: t("settings.resetSuccess")
      });
      setTimeout(() => {
        window.location.reload();
      }, 1500);
    } catch {
      toast({
        title: t("common.error"),
        description: t("settings.resetFailed"),
        variant: "destructive"
      });
    } finally {
      setIsResetting(false);
    }
  };
  return /* @__PURE__ */ jsxRuntimeExports.jsxs("div", { className: "space-y-6", children: [
    /* @__PURE__ */ jsxRuntimeExports.jsxs(Card, { children: [
      /* @__PURE__ */ jsxRuntimeExports.jsxs(CardHeader, { children: [
        /* @__PURE__ */ jsxRuntimeExports.jsxs(CardTitle, { className: "flex items-center gap-2", children: [
          /* @__PURE__ */ jsxRuntimeExports.jsx("div", { className: "h-7 w-7 rounded-lg bg-[var(--accent-primary)]/10 flex items-center justify-center", children: /* @__PURE__ */ jsxRuntimeExports.jsx(Database, { className: "h-4 w-4 text-[var(--accent-primary)]" }) }),
          t("settings.logSettings")
        ] }),
        /* @__PURE__ */ jsxRuntimeExports.jsx(CardDescription, { children: t("settings.logRetentionDays") })
      ] }),
      /* @__PURE__ */ jsxRuntimeExports.jsxs(CardContent, { className: "space-y-6", children: [
        /* @__PURE__ */ jsxRuntimeExports.jsxs("div", { className: "grid gap-4 md:grid-cols-3", children: [
          /* @__PURE__ */ jsxRuntimeExports.jsxs("div", { className: "space-y-2 p-3 rounded-xl bg-[var(--glass-bg)] border border-[var(--glass-border)]", children: [
            /* @__PURE__ */ jsxRuntimeExports.jsx(Label, { htmlFor: "log-level", children: t("settings.logLevel") }),
            /* @__PURE__ */ jsxRuntimeExports.jsxs(Select, { value: logLevel, onValueChange: (value) => setLogLevel(value), children: [
              /* @__PURE__ */ jsxRuntimeExports.jsx(SelectTrigger, { id: "log-level", children: /* @__PURE__ */ jsxRuntimeExports.jsx(SelectValue, { placeholder: t("settings.selectLogLevel") }) }),
              /* @__PURE__ */ jsxRuntimeExports.jsxs(SelectContent, { children: [
                /* @__PURE__ */ jsxRuntimeExports.jsx(SelectItem, { value: "debug", children: "璋冭瘯" }),
                /* @__PURE__ */ jsxRuntimeExports.jsx(SelectItem, { value: "info", children: "淇℃伅" }),
                /* @__PURE__ */ jsxRuntimeExports.jsx(SelectItem, { value: "warn", children: "璀﹀憡" }),
                /* @__PURE__ */ jsxRuntimeExports.jsx(SelectItem, { value: "error", children: "閿欒" })
              ] })
            ] }),
            /* @__PURE__ */ jsxRuntimeExports.jsx("p", { className: "text-xs text-muted-foreground", children: t("settings.logLevelHelp") })
          ] }),
          /* @__PURE__ */ jsxRuntimeExports.jsxs("div", { className: "space-y-2 p-3 rounded-xl bg-[var(--glass-bg)] border border-[var(--glass-border)]", children: [
            /* @__PURE__ */ jsxRuntimeExports.jsx(Label, { htmlFor: "log-retention", children: t("settings.logRetentionDays") }),
            /* @__PURE__ */ jsxRuntimeExports.jsx(
              Input,
              {
                id: "log-retention",
                type: "number",
                min: 1,
                max: 365,
                value: logRetentionDays,
                onChange: (e) => setLogRetentionDays(parseInt(e.target.value) || 30)
              }
            ),
            /* @__PURE__ */ jsxRuntimeExports.jsx("p", { className: "text-xs text-muted-foreground", children: t("settings.logRetentionHelp") })
          ] }),
          /* @__PURE__ */ jsxRuntimeExports.jsxs("div", { className: "space-y-2 p-3 rounded-xl bg-[var(--glass-bg)] border border-[var(--glass-border)]", children: [
            /* @__PURE__ */ jsxRuntimeExports.jsx(Label, { htmlFor: "max-logs", children: t("settings.maxLogs") }),
            /* @__PURE__ */ jsxRuntimeExports.jsx(
              Input,
              {
                id: "max-logs",
                type: "number",
                min: 100,
                max: 1e5,
                value: maxLogs,
                onChange: (e) => setMaxLogs(parseInt(e.target.value) || 1e4)
              }
            ),
            /* @__PURE__ */ jsxRuntimeExports.jsx("p", { className: "text-xs text-muted-foreground", children: t("settings.maxLogsHelp") })
          ] })
        ] }),
        /* @__PURE__ */ jsxRuntimeExports.jsxs("div", { className: "space-y-4", children: [
          /* @__PURE__ */ jsxRuntimeExports.jsxs("div", { className: "flex items-center justify-between rounded-xl border border-[var(--glass-border)] bg-[var(--glass-bg)] p-4", children: [
            /* @__PURE__ */ jsxRuntimeExports.jsxs("div", { className: "space-y-1", children: [
              /* @__PURE__ */ jsxRuntimeExports.jsx(Label, { htmlFor: "request-log-enabled", children: t("settings.requestLogEnabled") }),
              /* @__PURE__ */ jsxRuntimeExports.jsx("p", { className: "text-xs text-muted-foreground", children: t("settings.requestLogEnabledHelp") })
            ] }),
            /* @__PURE__ */ jsxRuntimeExports.jsx(
              Switch,
              {
                id: "request-log-enabled",
                checked: requestLogConfig.enabled,
                onCheckedChange: (checked) => {
                  void updateRequestLogConfig({ enabled: checked });
                }
              }
            )
          ] }),
          requestLogConfig.enabled ? /* @__PURE__ */ jsxRuntimeExports.jsxs("div", { className: "grid gap-4 md:grid-cols-2 xl:grid-cols-3", children: [
            /* @__PURE__ */ jsxRuntimeExports.jsxs("div", { className: "space-y-2 rounded-xl border border-[var(--glass-border)] bg-[var(--glass-bg)] p-3", children: [
              /* @__PURE__ */ jsxRuntimeExports.jsx(Label, { htmlFor: "request-log-max-entries", children: t("settings.requestLogMaxEntries") }),
              /* @__PURE__ */ jsxRuntimeExports.jsx(
                Input,
                {
                  id: "request-log-max-entries",
                  type: "number",
                  min: 0,
                  max: 1e4,
                  value: requestLogConfig.maxEntries,
                  onChange: (e) => {
                    void updateRequestLogConfig({ maxEntries: parseInt(e.target.value, 10) || 0 });
                  }
                }
              ),
              /* @__PURE__ */ jsxRuntimeExports.jsx("p", { className: "text-xs text-muted-foreground", children: t("settings.requestLogMaxEntriesHelp") })
            ] }),
            /* @__PURE__ */ jsxRuntimeExports.jsxs("div", { className: "flex items-center justify-between rounded-xl border border-[var(--glass-border)] bg-[var(--glass-bg)] p-3", children: [
              /* @__PURE__ */ jsxRuntimeExports.jsxs("div", { className: "space-y-1 pr-3", children: [
                /* @__PURE__ */ jsxRuntimeExports.jsx(Label, { htmlFor: "request-log-bodies", children: t("settings.requestLogIncludeBodies") }),
                /* @__PURE__ */ jsxRuntimeExports.jsx("p", { className: "text-xs text-muted-foreground", children: t("settings.requestLogIncludeBodiesHelp") })
              ] }),
              /* @__PURE__ */ jsxRuntimeExports.jsx(
                Switch,
                {
                  id: "request-log-bodies",
                  checked: requestLogConfig.includeBodies,
                  onCheckedChange: (checked) => {
                    void updateRequestLogConfig({
                      includeBodies: checked,
                      maxBodyChars: checked ? Math.max(requestLogConfig.maxBodyChars, 8e3) : requestLogConfig.maxBodyChars
                    });
                  }
                }
              )
            ] }),
            /* @__PURE__ */ jsxRuntimeExports.jsxs("div", { className: "flex items-center justify-between rounded-xl border border-[var(--glass-border)] bg-[var(--glass-bg)] p-3", children: [
              /* @__PURE__ */ jsxRuntimeExports.jsxs("div", { className: "space-y-1 pr-3", children: [
                /* @__PURE__ */ jsxRuntimeExports.jsx(Label, { htmlFor: "request-log-redact", children: t("settings.requestLogRedactSensitive") }),
                /* @__PURE__ */ jsxRuntimeExports.jsx("p", { className: "text-xs text-muted-foreground", children: t("settings.requestLogRedactSensitiveHelp") })
              ] }),
              /* @__PURE__ */ jsxRuntimeExports.jsx(
                Switch,
                {
                  id: "request-log-redact",
                  checked: requestLogConfig.redactSensitiveData,
                  onCheckedChange: (checked) => {
                    void updateRequestLogConfig({ redactSensitiveData: checked });
                  }
                }
              )
            ] }),
            requestLogConfig.includeBodies ? /* @__PURE__ */ jsxRuntimeExports.jsxs("div", { className: "space-y-2 rounded-xl border border-[var(--glass-border)] bg-[var(--glass-bg)] p-3 md:col-span-2 xl:col-span-3", children: [
              /* @__PURE__ */ jsxRuntimeExports.jsx(Label, { htmlFor: "request-log-max-body", children: t("settings.requestLogMaxBodyChars") }),
              /* @__PURE__ */ jsxRuntimeExports.jsx(
                Input,
                {
                  id: "request-log-max-body",
                  type: "number",
                  min: 0,
                  max: 1e6,
                  value: requestLogConfig.maxBodyChars,
                  onChange: (e) => {
                    void updateRequestLogConfig({ maxBodyChars: parseInt(e.target.value, 10) || 0 });
                  }
                }
              ),
              /* @__PURE__ */ jsxRuntimeExports.jsx("p", { className: "text-xs text-muted-foreground", children: t("settings.requestLogMaxBodyCharsHelp") })
            ] }) : null
          ] }) : null
        ] })
      ] })
    ] }),
    /* @__PURE__ */ jsxRuntimeExports.jsxs(Card, { children: [
      /* @__PURE__ */ jsxRuntimeExports.jsxs(CardHeader, { children: [
        /* @__PURE__ */ jsxRuntimeExports.jsxs(CardTitle, { className: "flex items-center gap-2", children: [
          /* @__PURE__ */ jsxRuntimeExports.jsx("div", { className: "h-7 w-7 rounded-lg bg-[var(--accent-primary)]/10 flex items-center justify-center", children: /* @__PURE__ */ jsxRuntimeExports.jsx(Download, { className: "h-4 w-4 text-[var(--accent-primary)]" }) }),
          t("settings.dataManagement")
        ] }),
        /* @__PURE__ */ jsxRuntimeExports.jsx(CardDescription, { children: t("settings.dataManagementDesc") })
      ] }),
      /* @__PURE__ */ jsxRuntimeExports.jsx(CardContent, { className: "space-y-6", children: /* @__PURE__ */ jsxRuntimeExports.jsxs("div", { className: "flex flex-wrap gap-2", children: [
        /* @__PURE__ */ jsxRuntimeExports.jsxs(
          Button,
          {
            variant: "outline",
            onClick: handleExportConfig,
            disabled: isExporting,
            className: "flex items-center gap-2",
            children: [
              /* @__PURE__ */ jsxRuntimeExports.jsx(Download, { className: "h-4 w-4" }),
              isExporting ? t("settings.exporting") : t("settings.exportConfig")
            ]
          }
        ),
        /* @__PURE__ */ jsxRuntimeExports.jsxs("div", { className: "relative", children: [
          /* @__PURE__ */ jsxRuntimeExports.jsx(
            "input",
            {
              type: "file",
              accept: ".json",
              onChange: handleImportConfig,
              className: "absolute inset-0 w-full h-full opacity-0 cursor-pointer",
              disabled: isImporting
            }
          ),
          /* @__PURE__ */ jsxRuntimeExports.jsxs(
            Button,
            {
              variant: "outline",
              disabled: isImporting,
              className: "flex items-center gap-2",
              children: [
                /* @__PURE__ */ jsxRuntimeExports.jsx(Upload, { className: "h-4 w-4" }),
                isImporting ? t("settings.importing") : t("settings.importConfig")
              ]
            }
          )
        ] })
      ] }) })
    ] }),
    /* @__PURE__ */ jsxRuntimeExports.jsxs(Card, { children: [
      /* @__PURE__ */ jsxRuntimeExports.jsxs(CardHeader, { children: [
        /* @__PURE__ */ jsxRuntimeExports.jsxs(CardTitle, { className: "flex items-center gap-2 text-destructive", children: [
          /* @__PURE__ */ jsxRuntimeExports.jsx(TriangleAlert, { className: "h-5 w-5" }),
          t("settings.dangerZone")
        ] }),
        /* @__PURE__ */ jsxRuntimeExports.jsx(CardDescription, { children: t("settings.dangerZoneDesc") })
      ] }),
      /* @__PURE__ */ jsxRuntimeExports.jsx(CardContent, { className: "space-y-6", children: /* @__PURE__ */ jsxRuntimeExports.jsxs("div", { className: "flex flex-wrap gap-2", children: [
        /* @__PURE__ */ jsxRuntimeExports.jsxs(
          Button,
          {
            variant: "outline",
            onClick: handleClearCache,
            disabled: isClearing,
            className: "flex items-center gap-2",
            children: [
              /* @__PURE__ */ jsxRuntimeExports.jsx(Trash2, { className: "h-4 w-4" }),
              isClearing ? t("settings.clearing") : t("settings.clearCache")
            ]
          }
        ),
        /* @__PURE__ */ jsxRuntimeExports.jsxs(Dialog, { children: [
          /* @__PURE__ */ jsxRuntimeExports.jsx(DialogTrigger, { asChild: true, children: /* @__PURE__ */ jsxRuntimeExports.jsxs(Button, { variant: "destructive", className: "flex items-center gap-2", children: [
            /* @__PURE__ */ jsxRuntimeExports.jsx(RotateCcw, { className: "h-4 w-4" }),
            t("settings.resetApp")
          ] }) }),
          /* @__PURE__ */ jsxRuntimeExports.jsxs(DialogContent, { children: [
            /* @__PURE__ */ jsxRuntimeExports.jsxs(DialogHeader, { children: [
              /* @__PURE__ */ jsxRuntimeExports.jsx(DialogTitle, { children: t("settings.confirmReset") }),
              /* @__PURE__ */ jsxRuntimeExports.jsx(DialogDescription, { children: t("settings.confirmResetDesc") })
            ] }),
            /* @__PURE__ */ jsxRuntimeExports.jsxs(DialogFooter, { children: [
              /* @__PURE__ */ jsxRuntimeExports.jsx(Button, { variant: "outline", onClick: () => {
              }, children: t("common.cancel") }),
              /* @__PURE__ */ jsxRuntimeExports.jsx(
                Button,
                {
                  variant: "destructive",
                  onClick: handleResetApp,
                  disabled: isResetting,
                  children: isResetting ? t("settings.resetting") : t("settings.confirmReset")
                }
              )
            ] })
          ] })
        ] })
      ] }) })
    ] })
  ] });
}
function SecuritySettings() {
  const { t } = useTranslation();
  const {
    credentialEncryption,
    setCredentialEncryption,
    logDesensitization,
    setLogDesensitization
  } = useSettingsStore();
  return /* @__PURE__ */ jsxRuntimeExports.jsxs("div", { className: "space-y-6", children: [
    /* @__PURE__ */ jsxRuntimeExports.jsxs(Card, { children: [
      /* @__PURE__ */ jsxRuntimeExports.jsxs(CardHeader, { children: [
        /* @__PURE__ */ jsxRuntimeExports.jsxs(CardTitle, { className: "flex items-center gap-2", children: [
          /* @__PURE__ */ jsxRuntimeExports.jsx(Lock, { className: "h-5 w-5" }),
          t("settings.credentialEncryption")
        ] }),
        /* @__PURE__ */ jsxRuntimeExports.jsx(CardDescription, { children: t("settings.credentialEncryptionHelp") })
      ] }),
      /* @__PURE__ */ jsxRuntimeExports.jsx(CardContent, { className: "space-y-6", children: /* @__PURE__ */ jsxRuntimeExports.jsxs("div", { className: "flex items-center justify-between", children: [
        /* @__PURE__ */ jsxRuntimeExports.jsx("div", { className: "space-y-0.5", children: /* @__PURE__ */ jsxRuntimeExports.jsx(Label, { htmlFor: "credential-encryption", children: t("settings.credentialEncryption") }) }),
        /* @__PURE__ */ jsxRuntimeExports.jsx(
          Switch,
          {
            id: "credential-encryption",
            checked: credentialEncryption,
            onCheckedChange: setCredentialEncryption
          }
        )
      ] }) })
    ] }),
    /* @__PURE__ */ jsxRuntimeExports.jsxs(Card, { children: [
      /* @__PURE__ */ jsxRuntimeExports.jsxs(CardHeader, { children: [
        /* @__PURE__ */ jsxRuntimeExports.jsxs(CardTitle, { className: "flex items-center gap-2", children: [
          /* @__PURE__ */ jsxRuntimeExports.jsx(FileText, { className: "h-5 w-5" }),
          t("settings.logDesensitization")
        ] }),
        /* @__PURE__ */ jsxRuntimeExports.jsx(CardDescription, { children: t("settings.logDesensitizationHelp") })
      ] }),
      /* @__PURE__ */ jsxRuntimeExports.jsxs(CardContent, { className: "space-y-6", children: [
        /* @__PURE__ */ jsxRuntimeExports.jsxs("div", { className: "flex items-center justify-between", children: [
          /* @__PURE__ */ jsxRuntimeExports.jsx("div", { className: "space-y-0.5", children: /* @__PURE__ */ jsxRuntimeExports.jsx(Label, { htmlFor: "log-desensitization", children: t("settings.logDesensitization") }) }),
          /* @__PURE__ */ jsxRuntimeExports.jsx(
            Switch,
            {
              id: "log-desensitization",
              checked: logDesensitization,
              onCheckedChange: setLogDesensitization
            }
          )
        ] }),
        /* @__PURE__ */ jsxRuntimeExports.jsxs("div", { className: "rounded-md bg-muted p-4", children: [
          /* @__PURE__ */ jsxRuntimeExports.jsx("p", { className: "text-sm font-medium mb-2", children: t("settings.example") }),
          /* @__PURE__ */ jsxRuntimeExports.jsxs("div", { className: "space-y-1 text-sm text-muted-foreground", children: [
            /* @__PURE__ */ jsxRuntimeExports.jsx("p", { children: "Original: sk-1234567890abcdef1234567890abcdef" }),
            /* @__PURE__ */ jsxRuntimeExports.jsx("p", { children: "Masked: sk-1234****cdef" })
          ] })
        ] })
      ] })
    ] })
  ] });
}
function ManagementApiSettings() {
  const { t } = useTranslation();
  const [config, setConfig] = reactExports.useState({
    enableManagementApi: false,
    managementApiSecret: ""
  });
  const [isLoading, setIsLoading] = reactExports.useState(true);
  const [isSaving, setIsSaving] = reactExports.useState(false);
  const [isGenerating, setIsGenerating] = reactExports.useState(false);
  const [showConfirmDialog, setShowConfirmDialog] = reactExports.useState(false);
  const [copied, setCopied] = reactExports.useState(false);
  const [proxyPort, setProxyPort] = reactExports.useState(8181);
  const [showSecret, setShowSecret] = reactExports.useState(false);
  reactExports.useEffect(() => {
    loadConfig();
  }, []);
  const loadConfig = async () => {
    try {
      const result = await window.electronAPI.invoke("managementApi:getConfig");
      if (result) {
        setConfig(result);
      }
      const appConfig = await window.electronAPI.config.get();
      if (appConfig?.proxyPort) {
        setProxyPort(appConfig.proxyPort);
      }
    } catch (error) {
      console.error("Failed to load management API config:", error);
    } finally {
      setIsLoading(false);
    }
  };
  const handleToggle = async (enabled) => {
    setIsSaving(true);
    try {
      await window.electronAPI.invoke("managementApi:updateConfig", {
        enableManagementApi: enabled
      });
      setConfig((prev) => ({ ...prev, enableManagementApi: enabled }));
    } catch (error) {
      console.error("Failed to update management API config:", error);
    } finally {
      setIsSaving(false);
    }
  };
  const handleCopy = async () => {
    if (!config.managementApiSecret) return;
    try {
      await navigator.clipboard.writeText(config.managementApiSecret);
      setCopied(true);
      setTimeout(() => setCopied(false), 2e3);
    } catch (error) {
      console.error("Failed to copy secret:", error);
    }
  };
  const handleGenerateNew = async () => {
    setIsGenerating(true);
    try {
      const newSecret = await window.electronAPI.invoke("managementApi:generateSecret");
      if (newSecret) {
        setConfig((prev) => ({ ...prev, managementApiSecret: newSecret }));
      }
      setShowConfirmDialog(false);
    } catch (error) {
      console.error("Failed to generate new secret:", error);
    } finally {
      setIsGenerating(false);
    }
  };
  const maskSecret = (secret) => {
    if (!secret || secret.length < 12) return secret;
    return secret.slice(0, 4) + "****" + secret.slice(-4);
  };
  const apiEndpoint = `http://127.0.0.1:${config.managementApiPort || proxyPort}/v0/management`;
  if (isLoading) {
    return /* @__PURE__ */ jsxRuntimeExports.jsx(Card, { children: /* @__PURE__ */ jsxRuntimeExports.jsx(CardContent, { className: "py-6", children: /* @__PURE__ */ jsxRuntimeExports.jsx("p", { className: "text-center text-muted-foreground", children: t("common.loading") }) }) });
  }
  return /* @__PURE__ */ jsxRuntimeExports.jsxs("div", { className: "space-y-6", children: [
    /* @__PURE__ */ jsxRuntimeExports.jsxs(Card, { children: [
      /* @__PURE__ */ jsxRuntimeExports.jsxs(CardHeader, { children: [
        /* @__PURE__ */ jsxRuntimeExports.jsxs(CardTitle, { className: "flex items-center gap-2", children: [
          /* @__PURE__ */ jsxRuntimeExports.jsx(Key, { className: "h-5 w-5" }),
          "璐熻浇鍧囪　"
        ] }),
        /* @__PURE__ */ jsxRuntimeExports.jsx(CardDescription, { children: t("settings.managementApi.description") })
      ] }),
      /* @__PURE__ */ jsxRuntimeExports.jsxs(CardContent, { className: "space-y-6", children: [
        /* @__PURE__ */ jsxRuntimeExports.jsxs("div", { className: "flex items-center justify-between", children: [
          /* @__PURE__ */ jsxRuntimeExports.jsxs("div", { className: "space-y-0.5", children: [
            /* @__PURE__ */ jsxRuntimeExports.jsx(Label, { htmlFor: "management-api-toggle", children: t("settings.managementApi.enableToggle") }),
            /* @__PURE__ */ jsxRuntimeExports.jsx("p", { className: "text-sm text-muted-foreground", children: t("settings.managementApi.enableDescription") })
          ] }),
          /* @__PURE__ */ jsxRuntimeExports.jsx(
            Switch,
            {
              id: "management-api-toggle",
              checked: config.enableManagementApi,
              onCheckedChange: handleToggle,
              disabled: isSaving
            }
          )
        ] }),
        /* @__PURE__ */ jsxRuntimeExports.jsxs("div", { className: "flex items-center gap-2", children: [
          /* @__PURE__ */ jsxRuntimeExports.jsxs("span", { className: "text-sm font-medium", children: [
            t("settings.managementApi.status"),
            ":"
          ] }),
          config.enableManagementApi ? /* @__PURE__ */ jsxRuntimeExports.jsx("span", { className: "text-sm text-green-600 dark:text-green-400", children: t("settings.managementApi.enabled") }) : /* @__PURE__ */ jsxRuntimeExports.jsx("span", { className: "text-sm text-muted-foreground", children: t("settings.managementApi.disabled") })
        ] })
      ] })
    ] }),
    config.enableManagementApi && /* @__PURE__ */ jsxRuntimeExports.jsxs(jsxRuntimeExports.Fragment, { children: [
      /* @__PURE__ */ jsxRuntimeExports.jsxs(Card, { children: [
        /* @__PURE__ */ jsxRuntimeExports.jsxs(CardHeader, { children: [
          /* @__PURE__ */ jsxRuntimeExports.jsx(CardTitle, { children: t("settings.managementApi.secretKey") }),
          /* @__PURE__ */ jsxRuntimeExports.jsx(CardDescription, { children: t("settings.managementApi.secretKeyDescription") })
        ] }),
        /* @__PURE__ */ jsxRuntimeExports.jsxs(CardContent, { className: "space-y-4", children: [
          /* @__PURE__ */ jsxRuntimeExports.jsxs(Alert, { children: [
            /* @__PURE__ */ jsxRuntimeExports.jsx(TriangleAlert, { className: "h-4 w-4" }),
            /* @__PURE__ */ jsxRuntimeExports.jsx(AlertDescription, { children: t("settings.managementApi.warning") })
          ] }),
          /* @__PURE__ */ jsxRuntimeExports.jsxs("div", { className: "flex items-center gap-2", children: [
            /* @__PURE__ */ jsxRuntimeExports.jsx(
              Input,
              {
                type: "text",
                value: config.managementApiSecret ? showSecret ? config.managementApiSecret : maskSecret(config.managementApiSecret) : t("settings.managementApi.secretKeyPlaceholder"),
                readOnly: true,
                className: "font-mono"
              }
            ),
            /* @__PURE__ */ jsxRuntimeExports.jsx(
              Button,
              {
                variant: "outline",
                size: "icon",
                onClick: () => setShowSecret(!showSecret),
                disabled: !config.managementApiSecret,
                title: showSecret ? t("settings.managementApi.hideSecret") : t("settings.managementApi.showSecret"),
                children: showSecret ? /* @__PURE__ */ jsxRuntimeExports.jsx(EyeOff, { className: "h-4 w-4" }) : /* @__PURE__ */ jsxRuntimeExports.jsx(Eye, { className: "h-4 w-4" })
              }
            ),
            /* @__PURE__ */ jsxRuntimeExports.jsx(
              Button,
              {
                variant: "outline",
                size: "icon",
                onClick: handleCopy,
                disabled: !config.managementApiSecret,
                title: t("settings.managementApi.copySecret"),
                children: copied ? /* @__PURE__ */ jsxRuntimeExports.jsx(Check, { className: "h-4 w-4" }) : /* @__PURE__ */ jsxRuntimeExports.jsx(Copy, { className: "h-4 w-4" })
              }
            )
          ] }),
          /* @__PURE__ */ jsxRuntimeExports.jsxs(
            Button,
            {
              variant: "outline",
              onClick: () => setShowConfirmDialog(true),
              disabled: isGenerating,
              children: [
                /* @__PURE__ */ jsxRuntimeExports.jsx(RefreshCw, { className: `h-4 w-4 mr-2 ${isGenerating ? "animate-spin" : ""}` }),
                t("settings.managementApi.generateNew")
              ]
            }
          )
        ] })
      ] }),
      /* @__PURE__ */ jsxRuntimeExports.jsxs(Card, { children: [
        /* @__PURE__ */ jsxRuntimeExports.jsxs(CardHeader, { children: [
          /* @__PURE__ */ jsxRuntimeExports.jsxs(CardTitle, { className: "flex items-center gap-2", children: [
            /* @__PURE__ */ jsxRuntimeExports.jsx(Terminal, { className: "h-5 w-5" }),
            t("settings.managementApi.apiDocumentation")
          ] }),
          /* @__PURE__ */ jsxRuntimeExports.jsx(CardDescription, { children: t("settings.managementApi.exampleUsage") })
        ] }),
        /* @__PURE__ */ jsxRuntimeExports.jsxs(CardContent, { className: "space-y-4", children: [
          /* @__PURE__ */ jsxRuntimeExports.jsxs("div", { className: "space-y-2", children: [
            /* @__PURE__ */ jsxRuntimeExports.jsx(Label, { children: t("settings.managementApi.apiEndpoint") }),
            /* @__PURE__ */ jsxRuntimeExports.jsx("code", { className: "block w-full rounded-md bg-muted p-3 text-sm font-mono break-all", children: apiEndpoint })
          ] }),
          /* @__PURE__ */ jsxRuntimeExports.jsxs("div", { className: "space-y-2", children: [
            /* @__PURE__ */ jsxRuntimeExports.jsxs(Label, { children: [
              "curl ",
              t("settings.managementApi.exampleUsage")
            ] }),
            /* @__PURE__ */ jsxRuntimeExports.jsx("pre", { className: "block w-full rounded-md bg-muted p-3 text-sm font-mono overflow-x-auto whitespace-pre-wrap break-all", children: `curl -X GET "${apiEndpoint}/config" \\
  -H "Authorization: Bearer YOUR_SECRET_KEY"` })
          ] }),
          /* @__PURE__ */ jsxRuntimeExports.jsxs("div", { className: "space-y-2", children: [
            /* @__PURE__ */ jsxRuntimeExports.jsxs(Label, { children: [
              "PUT ",
              t("settings.managementApi.exampleUsage")
            ] }),
            /* @__PURE__ */ jsxRuntimeExports.jsx("pre", { className: "block w-full rounded-md bg-muted p-3 text-sm font-mono overflow-x-auto whitespace-pre-wrap break-all", children: `curl -X PUT "${apiEndpoint}/config" \\
  -H "Authorization: Bearer YOUR_SECRET_KEY" \\
  -H "Content-Type: application/json" \\
  -d '{"proxyPort": 8181}'` })
          ] })
        ] })
      ] })
    ] }),
    /* @__PURE__ */ jsxRuntimeExports.jsx(Dialog, { open: showConfirmDialog, onOpenChange: setShowConfirmDialog, children: /* @__PURE__ */ jsxRuntimeExports.jsxs(DialogContent, { children: [
      /* @__PURE__ */ jsxRuntimeExports.jsxs(DialogHeader, { children: [
        /* @__PURE__ */ jsxRuntimeExports.jsx(DialogTitle, { children: t("settings.managementApi.generateNew") }),
        /* @__PURE__ */ jsxRuntimeExports.jsx(DialogDescription, { children: t("settings.managementApi.generateConfirm") })
      ] }),
      /* @__PURE__ */ jsxRuntimeExports.jsxs(DialogFooter, { children: [
        /* @__PURE__ */ jsxRuntimeExports.jsx(Button, { variant: "outline", onClick: () => setShowConfirmDialog(false), children: t("common.cancel") }),
        /* @__PURE__ */ jsxRuntimeExports.jsx(Button, { onClick: handleGenerateNew, disabled: isGenerating, children: isGenerating ? t("common.loading") : t("common.confirm") })
      ] })
    ] }) })
  ] });
}
function LoadBalanceMount() {
  return /* @__PURE__ */ jsxRuntimeExports.jsx("div", { id: "ds-loadbalance-root", className: "space-y-4" });
}
function Settings() {
  const { t } = useTranslation();
  const { fetchConfig, setConfig } = useSettingsStore();
  reactExports.useEffect(() => {
    void fetchConfig();
    const unsubscribe = window.electronAPI?.config?.onConfigChanged?.((config) => {
      setConfig(config);
    });
    return () => {
      unsubscribe?.();
    };
  }, [fetchConfig, setConfig]);
  return /* @__PURE__ */ jsxRuntimeExports.jsxs("div", { className: "space-y-6", children: [
    /* @__PURE__ */ jsxRuntimeExports.jsxs("div", { children: [
      /* @__PURE__ */ jsxRuntimeExports.jsx("h2", { className: "text-2xl font-bold tracking-tight", children: t("settings.title") }),
      /* @__PURE__ */ jsxRuntimeExports.jsx("p", { className: "text-muted-foreground", children: t("settings.description") })
    ] }),
    /* @__PURE__ */ jsxRuntimeExports.jsxs(Tabs, { defaultValue: "appearance", className: "w-full", children: [
      /* @__PURE__ */ jsxRuntimeExports.jsxs(TabsList, { className: "grid w-full grid-cols-5 h-auto", children: [
        /* @__PURE__ */ jsxRuntimeExports.jsxs(TabsTrigger, { value: "appearance", className: "flex items-center gap-2 py-2", children: [
          /* @__PURE__ */ jsxRuntimeExports.jsx(Sun, { className: "h-4 w-4" }),
          /* @__PURE__ */ jsxRuntimeExports.jsx("span", { className: "hidden sm:inline", children: t("settings.appearance") })
        ] }),
        /* @__PURE__ */ jsxRuntimeExports.jsxs(TabsTrigger, { value: "general", className: "flex items-center gap-2 py-2", children: [
          /* @__PURE__ */ jsxRuntimeExports.jsx(Settings$1, { className: "h-4 w-4" }),
          /* @__PURE__ */ jsxRuntimeExports.jsx("span", { className: "hidden sm:inline", children: t("settings.generalSettings") })
        ] }),
        /* @__PURE__ */ jsxRuntimeExports.jsxs(TabsTrigger, { value: "data", className: "flex items-center gap-2 py-2", children: [
          /* @__PURE__ */ jsxRuntimeExports.jsx(Database, { className: "h-4 w-4" }),
          /* @__PURE__ */ jsxRuntimeExports.jsx("span", { className: "hidden sm:inline", children: t("settings.data") })
        ] }),
        /* @__PURE__ */ jsxRuntimeExports.jsxs(TabsTrigger, { value: "security", className: "flex items-center gap-2 py-2", children: [
          /* @__PURE__ */ jsxRuntimeExports.jsx(Shield, { className: "h-4 w-4" }),
          /* @__PURE__ */ jsxRuntimeExports.jsx("span", { className: "hidden sm:inline", children: t("settings.security") })
        ] }),
        /* @__PURE__ */ jsxRuntimeExports.jsxs(TabsTrigger, { value: "loadbalance", className: "flex items-center gap-2 py-2", children: [
          /* @__PURE__ */ jsxRuntimeExports.jsx(Key, { className: "h-4 w-4" }),
          /* @__PURE__ */ jsxRuntimeExports.jsx("span", { className: "hidden sm:inline", children: "负载均衡" })
        ] })
      ] }),
      /* @__PURE__ */ jsxRuntimeExports.jsx(TabsContent, { value: "appearance", className: "mt-6", children: /* @__PURE__ */ jsxRuntimeExports.jsx(AppearanceSettings, {}) }),
      /* @__PURE__ */ jsxRuntimeExports.jsx(TabsContent, { value: "general", className: "mt-6", children: /* @__PURE__ */ jsxRuntimeExports.jsx(GeneralSettings, {}) }),
      /* @__PURE__ */ jsxRuntimeExports.jsx(TabsContent, { value: "data", className: "mt-6", children: /* @__PURE__ */ jsxRuntimeExports.jsx(DataManagement, {}) }),
      /* @__PURE__ */ jsxRuntimeExports.jsx(TabsContent, { value: "security", className: "mt-6", children: /* @__PURE__ */ jsxRuntimeExports.jsx(SecuritySettings, {}) }),
      /* @__PURE__ */ jsxRuntimeExports.jsx(TabsContent, { value: "loadbalance", className: "mt-6", children: /* @__PURE__ */ jsxRuntimeExports.jsx(LoadBalanceMount, {}) })
    ] })
  ] });
}
export {
  Settings
};
