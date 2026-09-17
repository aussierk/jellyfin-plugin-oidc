// Jellyfin.Plugin.OIDC/Configuration/src/strings.js
var STRINGS = {
  pageHeading: "OIDC RBAC Configuration",
  common: {
    removeBtn: "Remove"
  },
  tabs: {
    general: "General",
    providers: "Providers",
    roles: "Role Mappings"
  },
  general: {
    autoCreateUsers: "Auto-Create Users on First OIDC Login",
    migrateLocalUsers: "Migrate Local Users to SSO on First OIDC Login",
    migrateLocalUsersDesc: "Existing local accounts start signing in via SSO instead of their local password.",
    blockPrivateNetwork: "Block RFC1918/ULA Authorities",
    blockPrivateNetworkDesc: "Off by default since self-hosted identity providers often use them.",
    serverBaseUrlLabel: "Server Base URL (Override)",
    serverBaseUrlPlaceholder: "https://jellyfin.example.com",
    serverBaseUrlDesc: "Overrides the auto-detected host used for OIDC redirect and logout URLs."
  },
  access: {
    legend: "Access",
    allowedGroupsLabel: "Allowed Groups",
    allowedGroupsHint: "(one per line; matched against the role claim)",
    requireVerifiedEmailHtml: "Require a Verified Email (<code>email_verified</code>)",
    emailAllowlistWarningHtml: "&#9888; <strong>Ignored</strong> while &ldquo;Require a Verified Email&rdquo; above is unchecked.",
    allowedEmailDomainsLabel: "Allowed Email Domains",
    allowedEmailDomainsHint: "(one per line; subdomains match too)",
    allowedEmailsLabel: "Allowed Emails",
    allowedEmailsHint: "(exact addresses)",
    linkExistingUsersByEmail: "Link Accounts by Verified Email When the Subject Doesn't Match"
  },
  loginPage: {
    legend: "Login Page",
    manageLoginButtonBranding: "Manage the Login Button in Branding Automatically",
    manageLoginButtonBrandingDescHtml: "Syncs login buttons on every first-party client. Non-web apps sign in via Quick Connect.",
    statusLabel: "Status:",
    manualInstallSummary: "Manual Install (Copy/Paste)",
    manualInstallDesc: "Paste the HTML into Login Disclaimer and the CSS into Custom CSS.",
    copyHtmlBtn: "Copy Login Button HTML",
    copyCssBtn: "Copy Login Button CSS",
    hideManualLogin: "Hide the Username/Password Form on the Login Page",
    hideManualLoginDescHtml: "Hides the local login form and <strong>Forgot Password</strong> link; Quick Connect still works.",
    loginSubtitleLabel: "Login Instructions",
    loginSubtitleHint: "(optional)",
    loginSubtitlePlaceholder: "e.g. On a TV, choose Use Quick Connect and enter the code",
    loginTitleLabel: "Login Page Heading",
    defaultLoginTitle: "Please Sign In"
  },
  providersTab: {
    addProviderBtn: "+ Add Provider",
    emptyState: "No providers configured yet, so users can't sign in with SSO until you add one."
  },
  rolesTab: {
    legend: "RBAC Management",
    manageUserPolicy: "Manage User Policy from Role Mappings",
    manageUserPolicyDesc: "Turn off to manage users' Jellyfin policy manually (every field below becomes inert)",
    enableLibraryAccessManagement: "Manage Library Access from Role Mappings",
    enableLibraryAccessManagementHint: "(only while policy management is on)",
    fallbackRoleLabel: "Default Role",
    fallbackRoleHint: "(applied when a user's claim matches no role below)",
    addRoleMappingBtn: "+ Add Role Mapping",
    inactiveHint: "Policy management is off, role mappings and the default role above are not applied.",
    emptyState: "No role mappings, users receive the default role that is set above."
  },
  save: {
    saveBtn: "Save"
  },
  provider: {
    newProviderName: "New Provider",
    enabledLabel: "Enabled",
    connectionTitle: "Connection",
    connectionHint: "provider id, endpoint, client credentials & logout",
    claimMappingTitle: "Claim Mapping",
    claimMappingHint: "role, username, display name & avatar",
    appearanceTitle: "Appearance",
    appearanceHint: "login button color & icon",
    securityTitle: "Security",
    securityHint: "token validation, network guards, email-linking trust",
    prefillLabelPrefix: "Prefill for ",
    prefillHintHtml: "(claims, scopes &amp; icon; you&rsquo;ll still need to add the Issuer URL &amp; credentials)",
    prefillChooseOption: "Select an Identity Provider",
    providerIdLabel: "Provider ID",
    providerIdPlaceholder: "Unique Identifier (e.g. keycloak)",
    displayNameLabel: "Display Name",
    displayNamePlaceholder: "Shown on Login Button",
    issuerUrlLabel: "Issuer URL",
    issuerUrlPlaceholder: "https://idp.example.com/realms/myrealm",
    issuerHintPrefix: "Must match the issuer from discovery. ",
    issuerHintPinned: "Pinned. Please re-run Test Connection after editing.",
    issuerHintUnpinned: "Run Test Connection to pin it.",
    clientIdLabel: "Client ID",
    clientSecretLabel: "Client Secret",
    clientSecretPlaceholderPrefix: "Or reference an env var unique to this provider, e.g. ${",
    clientSecretFileLabel: "Client Secret File",
    clientSecretFilePlaceholder: "Optional: path to a file unique to this provider (e.g. a mounted Docker/K8s secret). Overrides Client Secret above.",
    scopesLabel: "Scopes",
    additionalParamsLabel: "Additional Parameters",
    additionalParamsPlaceholder: "key=value1&key2=value2 (extra /authorize params)",
    backchannelLogoutLabel: "Back-Channel Logout URL",
    backchannelLogoutHintHtml: "Register with your identity provider as <code>backchannel_logout_uri</code>.",
    copyBtn: "Copy",
    roleClaimLabel: "Role Claim Path",
    roleClaimPlaceholder: "e.g. groups or realm_access.roles",
    usernameClaimLabel: "Username Claim",
    displayNameClaimLabel: "Display Name Claim",
    emailClaimLabel: "Email Claim",
    pictureClaimLabel: "Picture Claim",
    pictureClaimPlaceholder: "e.g. picture",
    syncProfileImage: "Sync Profile Image",
    syncDisplayName: "Sync Display Name on Login",
    syncDisplayNameDescHtml: "<strong>Renames the Jellyfin account</strong> to match the claim on every login.",
    buttonColorLabel: "Button Color",
    resetToDefaultBtn: "Reset to Default",
    buttonIconLabel: "Button Icon",
    iconNone: "None",
    iconCustom: "Custom Image",
    iconCustomSet: "Custom Icon Set",
    iconCustomSetWithNamePrefix: "Custom Icon Set (",
    iconCustomSetWithNameSuffix: ")",
    strictAccessValidation: "Strict Access Token Validation",
    strictAccessValidationDesc: "Validates JWT access tokens against JWKS (opaque tokens skip this). Uncheck if your identity provider signs tokens with a key not published in its JWKS.",
    allowLoopback: "Allow Loopback Authority",
    allowLoopbackDesc: "Blocked by default (127.0.0.1, ::1). Enable only if intentional.",
    allowLinkLocal: "Allow Link-Local Authority",
    allowLinkLocalDesc: "Blocked by default (169.254.x.x, fe80::). Enable only if intentional.",
    trustedEmailLink: "Trusted for Email-Based Account Linking",
    trustedEmailLinkDesc: "Only matters when email-linking is on. Enable only for an identity provider you fully control; it never links to an admin account.",
    moveUpTitle: "Move up (changes login-button order)",
    moveDownTitle: "Move down (changes login-button order)",
    testConnectionBtn: "Test Connection"
  },
  presetLabels: {
    keycloak: "Keycloak",
    authentik: "Authentik",
    authelia: "Authelia",
    entra: "Microsoft Entra ID",
    google: "Google Workspace",
    okta: "Okta",
    auth0: "Auth0"
  },
  iconLabels: {
    github: "GitHub"
  },
  role: {
    newRoleName: "New Role",
    roleNamePrefix: "Role: ",
    adminBadge: "Admin",
    allProviders: "All Providers",
    allLibraries: "All Libraries",
    noLibraryAccess: "No Library Access",
    librarySingular: " Library",
    libraryPlural: " Libraries",
    roleNameLabel: "Role Name",
    roleNamePlaceholder: "Must match identity provider role claim value",
    providerFilterLabel: "Provider Filter ",
    providerFilterHint: "(blank = all providers)",
    permissionsLabel: "Permissions ",
    permissionsHint: "(Administrator grants everything below)",
    permissionsNote: "Multiple matching roles combine permissions; the strictest parental rating wins.",
    administrator: "Administrator",
    playbackGroup: "Playback",
    transcoding: "Transcoding",
    remoteAccess: "Remote Access",
    liveTvGroup: "Live TV",
    liveTvAccess: "Access",
    liveTvRecordingManagement: "Recording Management",
    contentManagementGroup: "Content Management",
    collections: "Collections",
    subtitles: "Subtitles",
    deleteContent: "Delete Content",
    libraryAccessTitle: "Library Access",
    specificLibrariesLabel: "Specific Libraries ",
    specificLibrariesHint: '(Only applies when "All libraries" is off)',
    selectLibraryOption: "Select Library",
    addLibraryBtn: "Add Library",
    maxParentalRatingLabel: "Max Parental Rating ",
    maxParentalRatingHint: "(empty = unrestricted)",
    unrestrictedOption: "Unrestricted",
    notDefinedSuffix: " (not defined on this server)",
    customScorePrefix: "Custom Score ",
    customScoreSuffix: " (select again to refresh)",
    noneOption: "None",
    notDefinedRoleSuffix: " (not a defined role)"
  },
  testConnection: {
    issuerRequired: "Issuer URL is required",
    testing: "Testing...",
    okPrefix: "OK, issuer ",
    scopeWarningPrefix: " (warning: scopes not advertised: ",
    scopeWarningSuffix: ")",
    failedPrefix: "Failed: ",
    dialogTitleOk: "Provider OK",
    dialogTitleFailed: "Provider Test Failed",
    unknownError: "Unknown Error",
    networkError: "Network Error",
    issuerLinePrefix: "Issuer: ",
    authorizeLinePrefix: "Authorize: ",
    tokenLinePrefix: "Token: ",
    userInfoLinePrefix: "UserInfo: ",
    scopeWarningBlockPrefix: "Warning: these requested scopes are not in scopes_supported:  "
  },
  branding: {
    installed: "Installed",
    notInstalled: "Not Installed",
    removeConfirm: "The plugin previously added an SSO login button to Branding (Login Disclaimer + Custom CSS). Remove it now? Cancel leaves it in place."
  },
  saveFlow: {
    duplicateIdTitle: "Duplicate Provider ID",
    duplicateIdMessagePrefix: "Provider ID(s) used by more than one provider: ",
    duplicateIdMessageSuffix: ". Each provider must have a unique Provider ID.",
    issuerChangedTitle: "Issuer URL changed",
    issuerChangedMessagePrefix: "Provider(s) with an edited, unverified Issuer URL: ",
    issuerChangedMessageSuffix: ". Run Test Connection to re-pin before saving.",
    unpinnedConfirmPrefix: "Provider(s) without endpoint pins: ",
    unpinnedConfirmSuffix: ". Endpoints will be trusted on first login. Run Test Connection first to avoid this. Save anyway?",
    saveFailedPrefix: "Failed to Save: ",
    unsavedChanges: "\u25CF Unsaved changes"
  },
  copyBtnCopied: "Copied"
};
var resolveCache = {};
function resolve(path) {
  if (Object.prototype.hasOwnProperty.call(resolveCache, path)) return resolveCache[path];
  var parts = path.split(".");
  var v = STRINGS;
  for (var i = 0; i < parts.length; i++) {
    v = v == null ? void 0 : v[parts[i]];
  }
  if (v == null) console.warn('OIDC RBAC: no STRINGS entry for "' + path + '"');
  resolveCache[path] = v;
  return v;
}
var STR_ATTRS = [
  ["data-str", function(elm, v) {
    elm.textContent = v;
  }],
  ["data-str-html", function(elm, v) {
    elm.innerHTML = v;
  }],
  ["data-str-placeholder", function(elm, v) {
    elm.setAttribute("placeholder", v);
  }],
  ["data-str-label", function(elm, v) {
    elm.setAttribute("label", v);
  }]
];
function applyStrings(view) {
  var selector = STR_ATTRS.map(function(pair) {
    return "[" + pair[0] + "]";
  }).join(",");
  view.querySelectorAll(selector).forEach(function(elm) {
    STR_ATTRS.forEach(function(pair) {
      var attr = pair[0], apply = pair[1];
      if (!elm.hasAttribute(attr)) return;
      var v = resolve(elm.getAttribute(attr));
      if (v != null) apply(elm, v);
    });
  });
}

// Jellyfin.Plugin.OIDC/Configuration/src/dom.js
function esc(str) {
  var d = document.createElement("div");
  d.textContent = str == null ? "" : String(str);
  return d.innerHTML.replace(/"/g, "&quot;").replace(/'/g, "&#39;");
}
var BOOL_ATTRS = { checked: 1, selected: 1, disabled: 1, open: 1, readonly: 1, hidden: 1 };
var VOID_TAGS = { input: 1, br: 1, hr: 1, img: 1 };
function el(tag, attrs, inner) {
  var html = "<" + tag;
  Object.keys(attrs || {}).forEach(function(name) {
    var v = attrs[name];
    if (v === false || v == null) return;
    if (BOOL_ATTRS[name]) {
      if (v) html += " " + name;
      return;
    }
    html += " " + name + '="' + esc(v) + '"';
  });
  if (VOID_TAGS[tag]) return html + " />";
  return html + ">" + (inner == null ? "" : inner) + "</" + tag + ">";
}
function gval(view, id) {
  var el2 = view.querySelector("#" + id);
  return el2 ? el2.value : "";
}
function gchk(view, id) {
  var el2 = view.querySelector("#" + id);
  return el2 ? el2.checked : false;
}
function sval(view, id, value) {
  var el2 = view.querySelector("#" + id);
  if (el2) el2.value = value;
}
function schk(view, id, checked) {
  var el2 = view.querySelector("#" + id);
  if (el2) el2.checked = checked;
}
function emptyState(msg) {
  return el("div", { class: "oidc-empty" }, esc(msg));
}
function copyToClipboard(view, srcId, btn) {
  var src = view.querySelector("#" + srcId);
  if (!src || !navigator.clipboard) return;
  navigator.clipboard.writeText(src.value).then(function() {
    var orig = btn.textContent;
    btn.textContent = STRINGS.copyBtnCopied;
    setTimeout(function() {
      btn.textContent = orig;
    }, 1200);
  }).catch(function() {
  });
}

// Jellyfin.Plugin.OIDC/Configuration/src/state.js
var pluginId = "e1c020c5-3972-4b7b-9538-ee4934cc902c";
var cfg = null;
var libs = {};
var ratings = [];
var dirty = false;
var dirtyView = null;
function setCfg(v) {
  cfg = v;
}
function setLibs(v) {
  libs = v;
}
function setRatings(v) {
  ratings = v;
}
function setDirtyView(v) {
  dirtyView = v;
}
function setDirty(v) {
  dirty = v;
  if (!dirtyView) return;
  var s = dirtyView.querySelector("#saveStatus");
  if (s) s.textContent = v ? STRINGS.saveFlow.unsavedChanges : "";
  var btn = dirtyView.querySelector("#btnSave");
  if (btn) btn.classList.toggle("oidc-save-dirty", v);
}
function beforeUnloadGuard(e) {
  if (!dirty) return void 0;
  e.preventDefault();
  e.returnValue = "";
  return "";
}

// Jellyfin.Plugin.OIDC/Configuration/src/utils.js
function listToText(arr) {
  return (arr || []).join("\n");
}
function textToList(str) {
  return (str || "").split(/[\n,]+/).map(function(s) {
    return s.trim();
  }).filter(Boolean);
}

// Jellyfin.Plugin.OIDC/Configuration/src/branding.js
var HTML_START = "<!-- oidc-sso-buttons:start -->";
var HTML_END = "<!-- oidc-sso-buttons:end -->";
var CSS_START = "/* oidc-sso-buttons:start */";
var CSS_END = "/* oidc-sso-buttons:end */";
function spliceRegion(text, startMarker, endMarker, block) {
  text = text || "";
  var s = text.indexOf(startMarker);
  var e = text.indexOf(endMarker);
  if (s !== -1 && e !== -1 && e > s) {
    var before = text.slice(0, s);
    var after = text.slice(e + endMarker.length);
    if (!block) {
      return (before + after).replace(/\n{3,}/g, "\n\n").trim();
    }
    return before + block + after;
  }
  if (!block) return text;
  return text.trim() ? text.trim() + "\n\n" + block : block;
}
function setBrandingStatus(view, installed) {
  var el2 = view.querySelector("#brandingStatus");
  if (el2) el2.textContent = installed ? STRINGS.branding.installed : STRINGS.branding.notInstalled;
}
function loadBrandingSnippet(view) {
  ApiClient.getJSON(ApiClient.getUrl("sso/OIDC/LoginButtonSnippet")).then(function(snip) {
    sval(view, "brandingHtml", snip && snip.Html || "");
    sval(view, "brandingCss", snip && snip.Css || "");
  }).catch(function() {
  });
  ApiClient.getNamedConfiguration("branding").then(function(b) {
    setBrandingStatus(view, (b && b.LoginDisclaimer || "").indexOf(HTML_START) !== -1);
  }).catch(function() {
  });
}
function syncBranding(view) {
  var manage = gchk(view, "manageLoginButtonBranding");
  var enabledCount = (cfg.Providers || []).filter(function(p) {
    return p.Enabled !== false;
  }).length;
  return Promise.all([
    ApiClient.getJSON(ApiClient.getUrl("sso/OIDC/LoginButtonSnippet")),
    ApiClient.getNamedConfiguration("branding")
  ]).then(function(res) {
    var snip = res[0] || {};
    var branding = res[1] || {};
    var present = (branding.LoginDisclaimer || "").indexOf(HTML_START) !== -1;
    var action;
    if (manage && enabledCount > 0) {
      action = "install";
    } else if (manage) {
      action = present ? "remove" : "none";
    } else if (present) {
      action = window.confirm(STRINGS.branding.removeConfirm) ? "remove" : "none";
    } else {
      action = "none";
    }
    if (action === "none") {
      setBrandingStatus(view, present);
      return;
    }
    var html = action === "install" ? snip.Html || "" : "";
    var css = action === "install" ? snip.Css || "" : "";
    var newDisclaimer = spliceRegion(branding.LoginDisclaimer, HTML_START, HTML_END, html);
    var newCss = spliceRegion(branding.CustomCss, CSS_START, CSS_END, css);
    if (newDisclaimer === (branding.LoginDisclaimer || "") && newCss === (branding.CustomCss || "")) {
      setBrandingStatus(view, action === "install");
      return;
    }
    branding.LoginDisclaimer = newDisclaimer;
    branding.CustomCss = newCss;
    return ApiClient.updateNamedConfiguration("branding", branding).then(function() {
      setBrandingStatus(view, action === "install");
    });
  }).catch(function(err) {
    console.error("OIDC RBAC: branding sync failed", err);
  });
}

// Jellyfin.Plugin.OIDC/Configuration/src/fields.js
function fld(label, type, id, value, placeholder, full) {
  return el(
    "div",
    { class: full ? "inputContainer full" : "inputContainer" },
    el("input", {
      is: "emby-input",
      type,
      id,
      value: String(value || ""),
      label,
      placeholder: placeholder || null,
      autocomplete: "off",
      autocapitalize: "off",
      spellcheck: "false"
    })
  );
}
function chk(id, label, checked) {
  return el(
    "label",
    null,
    el("input", { type: "checkbox", id, is: "emby-checkbox", checked: !!checked }) + " " + el("span", null, esc(label))
  );
}
function chkWithDesc(id, label, desc, checked) {
  return el(
    "div",
    { class: "checkboxContainer checkboxContainer-withDescription full" },
    el(
      "label",
      null,
      el("input", { type: "checkbox", id, is: "emby-checkbox", checked: !!checked }) + " " + el("span", null, esc(label))
    ) + el("div", { class: "fieldDescription" }, esc(desc))
  );
}
function permGroup(title, inner) {
  return el(
    "div",
    { class: "oidc-perm-group" },
    el("div", { class: "oidc-perm-title" }, esc(title)) + el("div", { class: "oidc-checkbox-row" }, inner)
  );
}

// Jellyfin.Plugin.OIDC/Configuration/src/providers.js
var DEFAULT_BUTTON_COLOR = "#4285F4";
function envVarSuggestion(p) {
  var slug = (p.ProviderId || "PROVIDER").toUpperCase().replace(/[^A-Z0-9]+/g, "_").replace(/^_+|_+$/g, "");
  return (slug || "PROVIDER") + "_CLIENT_SECRET";
}
var ICON_KEYS = ["authentik", "keycloak", "google", "microsoft", "okta", "auth0", "discord", "github"];
function iconIsCustom(v) {
  return !!v && ICON_KEYS.indexOf(v) === -1;
}
var ICON_LABELS = STRINGS.iconLabels;
var PROVIDER_PRESETS = {
  keycloak: { label: STRINGS.presetLabels.keycloak, roleClaim: "realm_access.roles", usernameClaim: "preferred_username", scopes: "openid profile email", icon: "keycloak" },
  authentik: { label: STRINGS.presetLabels.authentik, roleClaim: "groups", usernameClaim: "preferred_username", scopes: "openid profile email", icon: "authentik" },
  authelia: { label: STRINGS.presetLabels.authelia, roleClaim: "groups", usernameClaim: "preferred_username", scopes: "openid profile email groups", icon: "" },
  entra: { label: STRINGS.presetLabels.entra, roleClaim: "roles", usernameClaim: "preferred_username", scopes: "openid profile email", icon: "microsoft" },
  google: { label: STRINGS.presetLabels.google, roleClaim: "groups", usernameClaim: "email", scopes: "openid profile email", icon: "google" },
  okta: { label: STRINGS.presetLabels.okta, roleClaim: "groups", usernameClaim: "preferred_username", scopes: "openid profile email groups", icon: "okta" },
  auth0: { label: STRINGS.presetLabels.auth0, roleClaim: "", usernameClaim: "nickname", scopes: "openid profile email", icon: "auth0" }
};
function presetField(idx) {
  var opts = el("option", { value: "" }, STRINGS.provider.prefillChooseOption);
  Object.keys(PROVIDER_PRESETS).forEach(function(k) {
    opts += el("option", { value: k }, esc(PROVIDER_PRESETS[k].label));
  });
  return el(
    "div",
    { class: "selectContainer full" },
    el("label", { for: "prov_preset_" + idx }, STRINGS.provider.prefillLabelPrefix + el("span", { class: "fieldDescription" }, STRINGS.provider.prefillHintHtml)) + el("select", { is: "emby-select", id: "prov_preset_" + idx }, opts)
  );
}
function iconField(idx, cur) {
  var custom = iconIsCustom(cur);
  var opts = el("option", { value: "none", selected: !cur }, STRINGS.provider.iconNone);
  ICON_KEYS.forEach(function(k) {
    var label = ICON_LABELS[k] || k.charAt(0).toUpperCase() + k.slice(1);
    opts += el("option", { value: k, selected: cur === k }, label);
  });
  opts += el("option", { value: "custom", selected: custom }, STRINGS.provider.iconCustom);
  return el(
    "div",
    { class: "selectContainer full" },
    el("label", { for: "prov_icon_" + idx }, STRINGS.provider.buttonIconLabel) + el("select", { is: "emby-select", id: "prov_icon_" + idx }, opts) + el("input", { type: "hidden", id: "prov_icon_svg_" + idx, value: custom ? cur : "" }) + el("input", {
      type: "file",
      id: "prov_icon_file_" + idx,
      accept: ".svg,.png,.jpg,.jpeg,.gif,.webp,image/svg+xml,image/png,image/jpeg,image/gif,image/webp",
      class: "oidc-mt-sm" + (custom ? "" : " oidc-hidden")
    }) + el("span", { class: "fieldDescription", "data-icon-status": idx }, custom && cur ? STRINGS.provider.iconCustomSet : "")
  );
}
function provGroup(title, hint, inner, open) {
  var head = esc(title) + (hint ? " " + el("span", { class: "fieldDescription" }, esc(hint)) : "");
  return el(
    "details",
    { class: "oidc-section", open: !!open },
    el("summary", null, head) + el("div", { class: "oidc-grid" }, inner)
  );
}
function authorityHost(url) {
  if (!url) return "";
  try {
    return new URL(url).host;
  } catch (e) {
    return String(url).replace(/^[a-z][a-z0-9+.-]*:\/\//i, "").split("/")[0];
  }
}
function backchannelLogoutUrl(p, serverBaseUrl) {
  var base = (serverBaseUrl || "").replace(/\/+$/, "");
  if (!base) {
    try {
      base = ApiClient.serverAddress().replace(/\/+$/, "");
    } catch (e) {
      base = "";
    }
  }
  return (base || "(your server URL)") + "/sso/OIDC/BackchannelLogout/" + encodeURIComponent(p.ProviderId || "");
}
function renderProviders(view) {
  var container = view.querySelector("#providerList");
  container.innerHTML = "";
  if (!cfg.Providers.length) {
    container.innerHTML = emptyState(STRINGS.providersTab.emptyState);
    return;
  }
  cfg.Providers.forEach(function(p, idx) {
    var card = document.createElement("div");
    card.className = "oidc-item-card";
    var configured = !!(p.ProviderId && (p.PinnedIssuer || p.Authority) && p.ClientId);
    if (p.Enabled === false) card.className += " oidc-disabled";
    var connection = (configured ? "" : presetField(idx)) + fld(STRINGS.provider.providerIdLabel, "text", "prov_id_" + idx, p.ProviderId, STRINGS.provider.providerIdPlaceholder) + fld(STRINGS.provider.displayNameLabel, "text", "prov_name_" + idx, p.DisplayName, STRINGS.provider.displayNamePlaceholder) + el(
      "div",
      { class: "inputContainer full" },
      el("input", {
        is: "emby-input",
        type: "text",
        id: "prov_pinnedissuer_" + idx,
        value: p.PinnedIssuer || p.Authority || "",
        label: STRINGS.provider.issuerUrlLabel,
        placeholder: STRINGS.provider.issuerUrlPlaceholder,
        "data-verified": p.PinnedIssuer || "",
        autocomplete: "off",
        autocapitalize: "off",
        spellcheck: "false"
      }) + el("span", { class: "fieldDescription" }, STRINGS.provider.issuerHintPrefix + (p.PinnedIssuer ? STRINGS.provider.issuerHintPinned : STRINGS.provider.issuerHintUnpinned))
    ) + fld(STRINGS.provider.clientIdLabel, "text", "prov_clientid_" + idx, p.ClientId, "") + fld(
      STRINGS.provider.clientSecretLabel,
      "password",
      "prov_secret_" + idx,
      p.ClientSecret,
      STRINGS.provider.clientSecretPlaceholderPrefix + envVarSuggestion(p) + "}"
    ) + fld(
      STRINGS.provider.clientSecretFileLabel,
      "text",
      "prov_secretfile_" + idx,
      p.ClientSecretFile,
      STRINGS.provider.clientSecretFilePlaceholder
    ) + fld(STRINGS.provider.scopesLabel, "text", "prov_scopes_" + idx, p.Scopes || "openid profile email", "") + fld(STRINGS.provider.additionalParamsLabel, "text", "prov_params_" + idx, p.AdditionalParameters || "", STRINGS.provider.additionalParamsPlaceholder, true) + (p.ProviderId ? el(
      "div",
      { class: "inputContainer full" },
      el("input", {
        is: "emby-input",
        type: "text",
        id: "prov_bclogout_" + idx,
        readonly: true,
        value: backchannelLogoutUrl(p, cfg.ServerBaseUrl),
        label: STRINGS.provider.backchannelLogoutLabel,
        class: "oidc-mono-flex"
      }) + el("button", {
        is: "emby-button",
        type: "button",
        class: "oidc-btn-secondary oidc-mt-sm",
        "data-copy": "prov_bclogout_" + idx
      }, STRINGS.provider.copyBtn) + el("span", { class: "fieldDescription" }, STRINGS.provider.backchannelLogoutHintHtml)
    ) : "");
    var claims = fld(STRINGS.provider.roleClaimLabel, "text", "prov_roleclaim_" + idx, p.RoleClaim || "groups", STRINGS.provider.roleClaimPlaceholder) + fld(STRINGS.provider.usernameClaimLabel, "text", "prov_userclaim_" + idx, p.UsernameClaim || "preferred_username", "") + fld(STRINGS.provider.displayNameClaimLabel, "text", "prov_displayclaim_" + idx, p.DisplayNameClaim || "name", "") + fld(STRINGS.provider.emailClaimLabel, "text", "prov_emailclaim_" + idx, p.EmailClaim || "email", "") + fld(STRINGS.provider.pictureClaimLabel, "text", "prov_pictureclaim_" + idx, p.PictureClaim || "picture", STRINGS.provider.pictureClaimPlaceholder) + el(
      "div",
      { class: "checkboxContainer full" },
      el(
        "label",
        null,
        el("input", { type: "checkbox", id: "prov_syncimage_" + idx, is: "emby-checkbox", checked: p.SyncProfileImage !== false }) + " " + el("span", null, STRINGS.provider.syncProfileImage)
      )
    ) + el(
      "div",
      { class: "checkboxContainer checkboxContainer-withDescription full" },
      el(
        "label",
        null,
        el("input", { type: "checkbox", id: "prov_syncdisplay_" + idx, is: "emby-checkbox", checked: p.SyncDisplayName === true }) + " " + el("span", null, STRINGS.provider.syncDisplayName)
      ) + el("div", { class: "fieldDescription" }, STRINGS.provider.syncDisplayNameDescHtml)
    );
    var appearance = el(
      "div",
      { class: "inputContainer" },
      el("label", { for: "prov_color_" + idx }, STRINGS.provider.buttonColorLabel) + el(
        "div",
        { class: "oidc-inline-row" },
        el("input", { type: "color", id: "prov_color_" + idx, value: p.ButtonColor || DEFAULT_BUTTON_COLOR }) + el("button", { is: "emby-button", type: "button", class: "oidc-btn-secondary", "data-action": "reset-color", "data-idx": idx }, STRINGS.provider.resetToDefaultBtn)
      )
    ) + iconField(idx, p.ButtonIcon || "");
    var securityToggles = [
      { id: "prov_strict_access_", checked: p.StrictAccessTokenValidation !== false, label: STRINGS.provider.strictAccessValidation, desc: STRINGS.provider.strictAccessValidationDesc },
      { id: "prov_allow_loopback_", checked: p.AllowLoopbackAuthority === true, label: STRINGS.provider.allowLoopback, desc: STRINGS.provider.allowLoopbackDesc },
      { id: "prov_allow_linklocal_", checked: p.AllowLinkLocalAuthority === true, label: STRINGS.provider.allowLinkLocal, desc: STRINGS.provider.allowLinkLocalDesc },
      { id: "prov_trusted_email_link_", checked: p.TrustedForEmailLinking === true, label: STRINGS.provider.trustedEmailLink, desc: STRINGS.provider.trustedEmailLinkDesc }
    ];
    var security = securityToggles.map(function(t) {
      return chkWithDesc(t.id + idx, t.label, t.desc, t.checked);
    }).join("") + el("input", { type: "hidden", id: "prov_discovery_" + idx, value: p.Authority || "" }) + el("input", { type: "hidden", id: "prov_pinnedauthority_" + idx, value: p.PinnedAuthority || "" }) + el("input", { type: "hidden", id: "prov_pinnedtoken_" + idx, value: p.PinnedTokenEndpoint || "" }) + el("input", { type: "hidden", id: "prov_pinnedjwks_" + idx, value: p.PinnedJwksUri || "" }) + el("input", { type: "hidden", id: "prov_pinneduserinfo_" + idx, value: p.PinnedUserInfoEndpoint || "" }) + el("input", { type: "hidden", id: "prov_pinnedauthorize_" + idx, value: p.PinnedAuthorizeEndpoint || "" });
    var host = authorityHost(p.PinnedIssuer || p.Authority);
    card.innerHTML = el(
      "div",
      { class: "oidc-card-head" },
      el("h4", null, esc(p.DisplayName || STRINGS.provider.newProviderName)) + (host ? el("span", { class: "oidc-card-sub" }, esc(host)) : "") + el(
        "label",
        { class: "oidc-enable-toggle" },
        el("span", null, STRINGS.provider.enabledLabel) + el("input", { type: "checkbox", id: "prov_enabled_" + idx, checked: p.Enabled !== false })
      )
    ) + provGroup(STRINGS.provider.connectionTitle, STRINGS.provider.connectionHint, connection, !configured) + provGroup(STRINGS.provider.claimMappingTitle, STRINGS.provider.claimMappingHint, claims, false) + provGroup(STRINGS.provider.appearanceTitle, STRINGS.provider.appearanceHint, appearance, false) + provGroup(STRINGS.provider.securityTitle, STRINGS.provider.securityHint, security, false) + el(
      "div",
      { class: "oidc-row-actions" },
      el("button", {
        is: "emby-button",
        type: "button",
        class: "oidc-btn-secondary oidc-btn-icon",
        "data-action": "move-provider",
        "data-dir": "-1",
        "data-idx": idx,
        title: STRINGS.provider.moveUpTitle,
        disabled: idx === 0
      }, "&#8593;") + el("button", {
        is: "emby-button",
        type: "button",
        class: "oidc-btn-secondary oidc-btn-icon",
        "data-action": "move-provider",
        "data-dir": "1",
        "data-idx": idx,
        title: STRINGS.provider.moveDownTitle,
        disabled: idx === cfg.Providers.length - 1
      }, "&#8595;") + el("button", { is: "emby-button", type: "button", class: "oidc-btn-secondary", "data-action": "test-provider", "data-idx": idx }, STRINGS.provider.testConnectionBtn) + el("button", { is: "emby-button", type: "button", class: "oidc-btn-remove", "data-action": "remove-provider", "data-idx": idx }, STRINGS.common.removeBtn) + el("span", { class: "oidc-test-result", "data-idx": idx })
    );
    container.appendChild(card);
  });
}
function collectIcon(view, idx) {
  var kind = gval(view, "prov_icon_" + idx);
  if (kind === "custom") return (gval(view, "prov_icon_svg_" + idx) || "").trim();
  if (!kind || kind === "none") return "";
  return kind;
}
function collectProviders(view) {
  var result = [];
  view.querySelectorAll("#providerList .oidc-item-card").forEach(function(card, idx) {
    var issuerVal = gval(view, "prov_pinnedissuer_" + idx);
    var issuerEl = view.querySelector("#prov_pinnedissuer_" + idx);
    var verified = issuerEl ? issuerEl.dataset.verified || "" : "";
    var discoveryUrl = verified ? gval(view, "prov_discovery_" + idx) : issuerVal;
    result.push({
      ProviderId: gval(view, "prov_id_" + idx),
      DisplayName: gval(view, "prov_name_" + idx),
      Authority: discoveryUrl,
      ClientId: gval(view, "prov_clientid_" + idx),
      ClientSecret: gval(view, "prov_secret_" + idx),
      ClientSecretFile: gval(view, "prov_secretfile_" + idx),
      Scopes: gval(view, "prov_scopes_" + idx),
      RoleClaim: gval(view, "prov_roleclaim_" + idx),
      UsernameClaim: gval(view, "prov_userclaim_" + idx),
      DisplayNameClaim: gval(view, "prov_displayclaim_" + idx),
      EmailClaim: gval(view, "prov_emailclaim_" + idx),
      PictureClaim: gval(view, "prov_pictureclaim_" + idx),
      SyncProfileImage: gchk(view, "prov_syncimage_" + idx),
      SyncDisplayName: gchk(view, "prov_syncdisplay_" + idx),
      ButtonColor: gval(view, "prov_color_" + idx),
      AdditionalParameters: gval(view, "prov_params_" + idx),
      Enabled: gchk(view, "prov_enabled_" + idx),
      StrictAccessTokenValidation: gchk(view, "prov_strict_access_" + idx),
      AllowLoopbackAuthority: gchk(view, "prov_allow_loopback_" + idx),
      AllowLinkLocalAuthority: gchk(view, "prov_allow_linklocal_" + idx),
      TrustedForEmailLinking: gchk(view, "prov_trusted_email_link_" + idx),
      // Only the Test-Connection-verified issuer, never the raw box.
      PinnedIssuer: verified,
      PinnedTokenEndpoint: gval(view, "prov_pinnedtoken_" + idx),
      PinnedJwksUri: gval(view, "prov_pinnedjwks_" + idx),
      PinnedUserInfoEndpoint: gval(view, "prov_pinneduserinfo_" + idx),
      PinnedAuthorizeEndpoint: gval(view, "prov_pinnedauthorize_" + idx),
      // Hidden, written only by a successful Test Connection, never from the live Issuer box.
      PinnedAuthority: gval(view, "prov_pinnedauthority_" + idx),
      ButtonIcon: collectIcon(view, idx)
    });
  });
  return result;
}

// Jellyfin.Plugin.OIDC/Configuration/src/roles.js
function addLibChip(container, libId) {
  var chip = document.createElement("span");
  chip.className = "oidc-library-chip";
  chip.setAttribute("data-lib-id", libId);
  chip.innerHTML = esc(libs[libId] || libId) + ' <span class="remove">&times;</span>';
  container.appendChild(chip);
}
function ratingOptions(m) {
  var selName = (m.MaxParentalRatingName || "").trim();
  var legacy = typeof m.MaxParentalRating === "number" ? m.MaxParentalRating : null;
  if (!selName && legacy != null) {
    var hit = ratings.find(function(r) {
      return r.Score === legacy;
    });
    if (hit) {
      selName = hit.Name;
    }
  }
  var opts = '<option value="">' + esc(STRINGS.role.unrestrictedOption) + "</option>";
  var known = false;
  ratings.forEach(function(r) {
    var sel = r.Name.toLowerCase() === selName.toLowerCase() ? " selected" : "";
    if (sel) {
      known = true;
    }
    opts += '<option value="' + esc(r.Name) + '"' + sel + ">" + esc(r.Name) + "</option>";
  });
  if (selName && !known) {
    opts += '<option value="' + esc(selName) + '" selected>' + esc(selName) + esc(STRINGS.role.notDefinedSuffix) + "</option>";
  } else if (!selName && legacy != null) {
    opts += '<option value="__legacy__" selected disabled>' + esc(STRINGS.role.customScorePrefix) + legacy + esc(STRINGS.role.customScoreSuffix) + "</option>";
  }
  return opts;
}
function renderDefaultRoleOptions(view) {
  var sel = view.querySelector("#defaultRoleName");
  if (!sel) return;
  var current = sel.value || cfg.DefaultRoleName || "";
  var source = view.querySelector("#roleMappingList .oidc-item-card") ? collectRoleMappings(view).map(function(m) {
    return m.RoleName;
  }) : (cfg.RoleMappings || []).map(function(m) {
    return m.RoleName;
  });
  var names = [];
  source.forEach(function(n) {
    n = (n || "").trim();
    if (n && names.every(function(x) {
      return x.toLowerCase() !== n.toLowerCase();
    })) names.push(n);
  });
  var opts = '<option value="">' + esc(STRINGS.role.noneOption) + "</option>";
  if (current && names.every(function(x) {
    return x.toLowerCase() !== current.toLowerCase();
  })) {
    opts += '<option value="' + esc(current) + '">' + esc(current) + esc(STRINGS.role.notDefinedRoleSuffix) + "</option>";
  }
  names.forEach(function(n) {
    opts += '<option value="' + esc(n) + '">' + esc(n) + "</option>";
  });
  sel.innerHTML = opts;
  sel.value = current;
}
function renderRoleMappings(view) {
  var container = view.querySelector("#roleMappingList");
  container.innerHTML = "";
  if (!cfg.RoleMappings.length) {
    container.innerHTML = emptyState(STRINGS.rolesTab.emptyState);
    renderDefaultRoleOptions(view);
    return;
  }
  cfg.RoleMappings.forEach(function(m, idx) {
    var card = document.createElement("details");
    card.className = "oidc-item-card oidc-role";
    var libOpts = Object.keys(libs).map(function(id) {
      return el("option", { value: id }, esc(libs[id]));
    }).join("");
    var selectedLibs = (m.LibraryIds || []).concat(
      (m.LibraryNames || []).map(function(name) {
        var f = Object.keys(libs).find(function(id) {
          return libs[id].toLowerCase() === name.toLowerCase();
        });
        return f || name;
      })
    );
    var provOpts = el("option", { value: "", selected: !m.ProviderFilter }, STRINGS.role.allProviders) + (cfg.Providers || []).map(function(p) {
      return el(
        "option",
        { value: p.ProviderId, selected: m.ProviderFilter === p.ProviderId },
        esc(p.DisplayName || p.ProviderId)
      );
    }).join("");
    var provLabel = m.ProviderFilter ? ((cfg.Providers || []).find(function(p) {
      return p.ProviderId === m.ProviderFilter;
    }) || {}).DisplayName || m.ProviderFilter : STRINGS.role.allProviders;
    var libLabel = m.EnableAllLibraries ? STRINGS.role.allLibraries : selectedLibs.length ? selectedLibs.length + (selectedLibs.length === 1 ? STRINGS.role.librarySingular : STRINGS.role.libraryPlural) : STRINGS.role.noLibraryAccess;
    var scopeParts = [provLabel, libLabel];
    card.innerHTML = el(
      "summary",
      { class: "oidc-role-summary" },
      el("h4", null, STRINGS.role.roleNamePrefix + esc(m.RoleName || STRINGS.role.newRoleName)) + (m.IsAdmin ? el("span", { class: "oidc-badge" }, STRINGS.role.adminBadge) : "") + el("span", { class: "oidc-role-scope" }, esc(scopeParts.join("  \xB7  ")))
    ) + fld(STRINGS.role.roleNameLabel, "text", "role_name_" + idx, m.RoleName, STRINGS.role.roleNamePlaceholder, true) + el(
      "div",
      { class: "selectContainer full oidc-mb-md" },
      el("label", null, STRINGS.role.providerFilterLabel + el("span", { class: "fieldDescription" }, STRINGS.role.providerFilterHint)) + el("select", { is: "emby-select", id: "role_provfilter_" + idx }, provOpts)
    ) + el(
      "div",
      { class: "oidc-field full oidc-mt-sm" },
      el("label", null, STRINGS.role.permissionsLabel + el("span", { class: "fieldDescription" }, STRINGS.role.permissionsHint)) + el("p", { class: "fieldDescription oidc-hint-tight" }, STRINGS.role.permissionsNote) + el("div", { class: "oidc-checkbox-row oidc-mt-xs" }, chk("role_admin_" + idx, STRINGS.role.administrator, m.IsAdmin)) + permGroup(
        STRINGS.role.playbackGroup,
        chk("role_playback_" + idx, STRINGS.role.playbackGroup, m.EnableMediaPlayback !== false) + chk("role_transcode_" + idx, STRINGS.role.transcoding, m.EnableTranscoding !== false) + chk("role_remote_" + idx, STRINGS.role.remoteAccess, m.EnableRemoteAccess !== false)
      ) + permGroup(
        STRINGS.role.liveTvGroup,
        chk("role_livetv_" + idx, STRINGS.role.liveTvAccess, m.EnableLiveTv) + chk("role_livetvmgmt_" + idx, STRINGS.role.liveTvRecordingManagement, m.EnableLiveTvManagement)
      ) + permGroup(
        STRINGS.role.contentManagementGroup,
        chk("role_collections_" + idx, STRINGS.role.collections, m.EnableCollectionManagement) + chk("role_subtitles_" + idx, STRINGS.role.subtitles, m.EnableSubtitleManagement) + chk("role_delete_" + idx, STRINGS.role.deleteContent, m.EnableContentDeletion)
      )
    ) + el(
      "div",
      { class: "oidc-field full oidc-mt-md" },
      el("div", { class: "oidc-perm-title" }, STRINGS.role.libraryAccessTitle) + el("div", { class: "oidc-checkbox-row" }, chk("role_alllibs_" + idx, STRINGS.role.allLibraries, m.EnableAllLibraries)) + el("label", { class: "oidc-mt-sm2" }, STRINGS.role.specificLibrariesLabel + el("span", { class: "fieldDescription" }, STRINGS.role.specificLibrariesHint)) + el("select", { is: "emby-select", id: "role_libadd_" + idx }, el("option", { value: "" }, STRINGS.role.selectLibraryOption) + libOpts) + el("button", { is: "emby-button", type: "button", class: "oidc-btn-secondary oidc-mt-sm oidc-w-fit", "data-action": "add-lib", "data-idx": idx }, STRINGS.role.addLibraryBtn) + el("div", { id: "role_libs_" + idx, class: "oidc-library-list" })
    ) + el(
      "div",
      { class: "selectContainer oidc-mt-md" },
      el("label", null, STRINGS.role.maxParentalRatingLabel + el("span", { class: "fieldDescription" }, STRINGS.role.maxParentalRatingHint)) + el("select", { is: "emby-select", id: "role_maxrating_" + idx }, ratingOptions(m))
    ) + el(
      "div",
      { class: "oidc-mt-md" },
      el("button", { is: "emby-button", type: "button", class: "oidc-btn-remove", "data-action": "remove-role", "data-idx": idx }, STRINGS.common.removeBtn)
    );
    card.open = !m.RoleName;
    container.appendChild(card);
    var libCont = view.querySelector("#role_libs_" + idx);
    selectedLibs.forEach(function(libId) {
      addLibChip(libCont, libId);
    });
  });
  renderDefaultRoleOptions(view);
}
function collectRoleMappings(view) {
  var result = [];
  view.querySelectorAll("#roleMappingList .oidc-item-card").forEach(function(card, idx) {
    var chips = view.querySelectorAll("#role_libs_" + idx + " .oidc-library-chip");
    var libIds = [];
    chips.forEach(function(c) {
      libIds.push(c.getAttribute("data-lib-id"));
    });
    var mr = gval(view, "role_maxrating_" + idx);
    var mrName = mr && mr !== "__legacy__" ? mr : "";
    var mrLegacy = mrName ? null : cfg.RoleMappings[idx] ? cfg.RoleMappings[idx].MaxParentalRating : null;
    result.push({
      RoleName: gval(view, "role_name_" + idx),
      ProviderFilter: gval(view, "role_provfilter_" + idx),
      IsAdmin: gchk(view, "role_admin_" + idx),
      EnableAllLibraries: gchk(view, "role_alllibs_" + idx),
      LibraryIds: libIds,
      LibraryNames: [],
      EnableLiveTv: gchk(view, "role_livetv_" + idx),
      EnableLiveTvManagement: gchk(view, "role_livetvmgmt_" + idx),
      EnableMediaPlayback: gchk(view, "role_playback_" + idx),
      EnableRemoteAccess: gchk(view, "role_remote_" + idx),
      EnableTranscoding: gchk(view, "role_transcode_" + idx),
      EnableContentDeletion: gchk(view, "role_delete_" + idx),
      EnableCollectionManagement: gchk(view, "role_collections_" + idx),
      EnableSubtitleManagement: gchk(view, "role_subtitles_" + idx),
      MaxParentalRatingName: mrName,
      MaxParentalRating: mrLegacy
    });
  });
  return result;
}

// Jellyfin.Plugin.OIDC/Configuration/src/uiToggles.js
function updateRbacManagementUi(view) {
  var managed = view.querySelector("#manageUserPolicy").checked;
  var list = view.querySelector("#roleMappingList");
  var fallback = view.querySelector("#defaultRoleName");
  var addBtn = view.querySelector("#btnAddRoleMapping");
  var libraryAccess = view.querySelector("#enableLibraryAccessManagement");
  var hint = view.querySelector("#roleMappingsInactiveHint");
  [list, fallback, addBtn, libraryAccess].forEach(function(node) {
    if (node) node.classList.toggle("oidc-dimmed", !managed);
  });
  if (fallback) fallback.disabled = !managed;
  if (addBtn) addBtn.disabled = !managed;
  if (libraryAccess) libraryAccess.disabled = !managed;
  if (hint) hint.hidden = managed;
}
function updateEmailAllowlistUi(view) {
  var active = view.querySelector("#requireVerifiedEmail").checked;
  var fields = view.querySelector("#emailAllowlistFields");
  if (!fields) return;
  fields.querySelectorAll(".oidc-allowlist-field").forEach(function(field) {
    field.classList.toggle("oidc-dimmed", !active);
  });
  fields.querySelectorAll("textarea").forEach(function(textarea) {
    textarea.disabled = !active;
  });
  var warn = view.querySelector("#emailAllowlistInertWarning");
  if (warn) {
    var hasContent = Array.prototype.some.call(
      fields.querySelectorAll("textarea"),
      function(t) {
        return t.value.trim() !== "";
      }
    );
    warn.hidden = active || !hasContent;
  }
}

// Jellyfin.Plugin.OIDC/Configuration/src/testConnection.js
function setTestStatus(resultEl, variant, text) {
  if (!resultEl) return;
  resultEl.className = "oidc-test-result" + (variant ? " oidc-status--" + variant : "");
  resultEl.textContent = text;
}
function testProvider(view, idx) {
  var issuerEl = view.querySelector("#prov_pinnedissuer_" + idx);
  var authority = issuerEl ? issuerEl.value : "";
  var scopes = gval(view, "prov_scopes_" + idx);
  var resultEl = view.querySelector('.oidc-test-result[data-idx="' + idx + '"]');
  if (!authority) {
    setTestStatus(resultEl, "error", STRINGS.testConnection.issuerRequired);
    return;
  }
  setTestStatus(resultEl, "dim", STRINGS.testConnection.testing);
  var allowLoopback = gchk(view, "prov_allow_loopback_" + idx);
  var allowLinkLocal = gchk(view, "prov_allow_linklocal_" + idx);
  ApiClient.ajax({
    type: "POST",
    url: ApiClient.getUrl("sso/OIDC/Config/TestProvider"),
    data: JSON.stringify({
      Authority: authority,
      Scopes: scopes,
      AllowLoopbackAuthority: allowLoopback,
      AllowLinkLocalAuthority: allowLinkLocal
    }),
    contentType: "application/json",
    dataType: "json"
  }).then(function(result) {
    if (result.Success) {
      var canonicalIssuer = result.Issuer || authority;
      cfg.Providers[idx].Authority = authority;
      cfg.Providers[idx].PinnedAuthority = authority;
      cfg.Providers[idx].PinnedIssuer = canonicalIssuer;
      cfg.Providers[idx].PinnedTokenEndpoint = result.TokenEndpoint || "";
      cfg.Providers[idx].PinnedJwksUri = result.JwksUri || "";
      cfg.Providers[idx].PinnedUserInfoEndpoint = result.UserInfoEndpoint || "";
      cfg.Providers[idx].PinnedAuthorizeEndpoint = result.AuthorizationEndpoint || "";
      sval(view, "prov_discovery_" + idx, authority);
      sval(view, "prov_pinnedauthority_" + idx, authority);
      sval(view, "prov_pinnedtoken_" + idx, result.TokenEndpoint || "");
      sval(view, "prov_pinnedjwks_" + idx, result.JwksUri || "");
      sval(view, "prov_pinneduserinfo_" + idx, result.UserInfoEndpoint || "");
      sval(view, "prov_pinnedauthorize_" + idx, result.AuthorizationEndpoint || "");
      if (issuerEl) {
        issuerEl.value = canonicalIssuer;
        issuerEl.dataset.verified = canonicalIssuer;
      }
      var sec = issuerEl && issuerEl.closest("details.oidc-section");
      if (sec) sec.open = true;
      setDirty(true);
      var msg = STRINGS.testConnection.okPrefix + result.Issuer;
      var hasScopeWarning = result.UnsupportedRequestedScopes && result.UnsupportedRequestedScopes.length > 0;
      if (hasScopeWarning) {
        msg += STRINGS.testConnection.scopeWarningPrefix + result.UnsupportedRequestedScopes.join(", ") + STRINGS.testConnection.scopeWarningSuffix;
      }
      setTestStatus(resultEl, hasScopeWarning ? "warn" : "ok", msg);
      Dashboard.alert({
        title: STRINGS.testConnection.dialogTitleOk,
        message: STRINGS.testConnection.issuerLinePrefix + result.Issuer + "\n" + STRINGS.testConnection.authorizeLinePrefix + result.AuthorizationEndpoint + "\n" + STRINGS.testConnection.tokenLinePrefix + result.TokenEndpoint + "\n" + (result.UserInfoEndpoint ? STRINGS.testConnection.userInfoLinePrefix + result.UserInfoEndpoint + "\n" : "") + (result.UnsupportedRequestedScopes && result.UnsupportedRequestedScopes.length > 0 ? STRINGS.testConnection.scopeWarningBlockPrefix + result.UnsupportedRequestedScopes.join(", ") : "")
      });
    } else {
      setTestStatus(resultEl, "error", STRINGS.testConnection.failedPrefix + result.Error);
      Dashboard.alert({ title: STRINGS.testConnection.dialogTitleFailed, message: result.Error || STRINGS.testConnection.unknownError });
    }
  }).catch(function(err) {
    var msg = err && (err.statusText || err.message) || STRINGS.testConnection.networkError;
    setTestStatus(resultEl, "error", STRINGS.testConnection.failedPrefix + msg);
    Dashboard.alert({ title: STRINGS.testConnection.dialogTitleFailed, message: msg });
  });
}

// Jellyfin.Plugin.OIDC/Configuration/src/index.js
function autogrowTextarea(el2) {
  if (!el2) return;
  el2.style.height = "auto";
  el2.style.height = el2.scrollHeight + "px";
}
function index_default(view) {
  applyStrings(view);
  setDirtyView(view);
  window.addEventListener("beforeunload", beforeUnloadGuard);
  view.addEventListener("input", function(e) {
    setDirty(true);
    if (e.target && e.target.classList && e.target.classList.contains("oidc-autogrow")) autogrowTextarea(e.target);
    if (e.target && e.target.id && e.target.id.indexOf("role_name_") === 0) renderDefaultRoleOptions(view);
  }, true);
  view.addEventListener("change", function() {
    setDirty(true);
  }, true);
  view.querySelector("#manageUserPolicy").addEventListener("change", function() {
    updateRbacManagementUi(view);
  });
  view.querySelector("#requireVerifiedEmail").addEventListener("change", function() {
    updateEmailAllowlistUi(view);
  });
  var savebar = view.querySelector(".oidc-savebar");
  var contentPrimary = view.querySelector(".content-primary");
  function alignSaveBar() {
    if (!savebar || !contentPrimary) return;
    var r = contentPrimary.getBoundingClientRect();
    if (r.width < 1) return;
    savebar.style.left = r.left + "px";
    savebar.style.width = r.width + "px";
    var pad = savebar.offsetHeight + 12 + "px";
    if (contentPrimary.style.paddingBottom !== pad) {
      contentPrimary.style.paddingBottom = pad;
    }
  }
  if (savebar) {
    var bg = getComputedStyle(document.body).backgroundColor;
    savebar.style.background = bg && bg !== "rgba(0, 0, 0, 0)" && bg !== "transparent" ? bg : "#101010";
  }
  if (window.ResizeObserver && contentPrimary) {
    new ResizeObserver(alignSaveBar).observe(contentPrimary);
  }
  view.addEventListener("viewbeforehide", function() {
    setDirty(false);
    window.removeEventListener("resize", alignSaveBar);
  });
  view.addEventListener("viewshow", function() {
    Dashboard.showLoadingMsg();
    window.addEventListener("resize", alignSaveBar);
    Promise.all([
      ApiClient.getJSON(ApiClient.getUrl("sso/OIDC/Config/Libraries")).catch(function() {
        return {};
      }),
      ApiClient.getJSON(ApiClient.getUrl("sso/OIDC/Config/Ratings")).catch(function() {
        return [];
      })
    ]).then(function(results) {
      setLibs(results[0] || {});
      setRatings(results[1] || []);
    }).then(function() {
      return ApiClient.getPluginConfiguration(pluginId);
    }).then(function(config) {
      config.Providers = config.Providers || [];
      config.RoleMappings = config.RoleMappings || [];
      setCfg(config);
      renderProviders(view);
      renderRoleMappings(view);
      schk(view, "autoCreateUsers", cfg.AutoCreateUsers !== false);
      schk(view, "migrateLocalUsers", cfg.MigrateLocalUsers === true);
      schk(view, "blockPrivateNetworkAuthorities", cfg.BlockPrivateNetworkAuthorities === true);
      sval(view, "allowedGroups", listToText(cfg.AllowedGroups));
      schk(view, "requireVerifiedEmail", cfg.RequireVerifiedEmail === true);
      sval(view, "allowedEmailDomains", listToText(cfg.AllowedEmailDomains));
      sval(view, "allowedEmails", listToText(cfg.AllowedEmails));
      schk(view, "linkExistingUsersByEmail", cfg.LinkExistingUsersByEmail === true);
      schk(view, "manageUserPolicy", cfg.ManageUserPolicy !== false);
      schk(view, "enableLibraryAccessManagement", cfg.EnableLibraryAccessManagement !== false);
      updateRbacManagementUi(view);
      updateEmailAllowlistUi(view);
      schk(view, "manageLoginButtonBranding", cfg.ManageLoginButtonBranding !== false);
      schk(view, "hideManualLogin", cfg.HideManualLogin === true);
      sval(view, "loginTitle", cfg.LoginTitle || STRINGS.loginPage.defaultLoginTitle);
      sval(view, "loginSubtitle", cfg.LoginSubtitle || "");
      autogrowTextarea(view.querySelector("#loginSubtitle"));
      sval(view, "serverBaseUrl", cfg.ServerBaseUrl || "");
      loadBrandingSnippet(view);
      setDirty(false);
      alignSaveBar();
      Dashboard.hideLoadingMsg();
    }).catch(function(err) {
      Dashboard.hideLoadingMsg();
      console.error("OIDC RBAC: failed to load config", err);
    });
  });
  var tabsEl = view.querySelector(".oidc-tabs");
  var tabButtons = tabsEl.querySelectorAll(".emby-tab-button");
  tabButtons.forEach(function(btn, i) {
    if (Number(btn.getAttribute("data-index")) !== i) {
      console.warn("OIDC RBAC: tab button data-index does not match DOM position at index " + i);
    }
  });
  tabsEl.addEventListener("tabchange", function(e) {
    var idx = e.detail.selectedTabIndex;
    tabButtons.forEach(function(btn, i) {
      btn.setAttribute("aria-selected", i === idx ? "true" : "false");
    });
    view.querySelectorAll(".tabContent").forEach(function(c) {
      c.classList.remove("is-active");
    });
    var content = view.querySelector("#tab-" + tabButtons[idx].getAttribute("data-tab"));
    content.classList.add("is-active");
    content.querySelectorAll(".oidc-autogrow").forEach(autogrowTextarea);
  });
  view.querySelectorAll("[data-copy]").forEach(function(btn) {
    btn.addEventListener("click", function() {
      copyToClipboard(view, btn.getAttribute("data-copy"), btn);
    });
  });
  view.querySelector("#btnAddProvider").addEventListener("click", function() {
    if (!cfg) return;
    cfg.Providers = collectProviders(view);
    cfg.Providers.push({
      ProviderId: "",
      DisplayName: STRINGS.provider.newProviderName,
      Authority: "",
      ClientId: "",
      ClientSecret: "",
      ClientSecretFile: "",
      Scopes: "openid profile email",
      RoleClaim: "groups",
      UsernameClaim: "preferred_username",
      DisplayNameClaim: "name",
      EmailClaim: "email",
      PictureClaim: "picture",
      SyncProfileImage: true,
      SyncDisplayName: false,
      Enabled: true,
      ButtonColor: DEFAULT_BUTTON_COLOR,
      ButtonIcon: "",
      AdditionalParameters: "",
      StrictAccessTokenValidation: true,
      AllowLoopbackAuthority: false,
      AllowLinkLocalAuthority: false,
      TrustedForEmailLinking: false,
      PinnedAuthority: "",
      PinnedIssuer: "",
      PinnedTokenEndpoint: "",
      PinnedJwksUri: "",
      PinnedUserInfoEndpoint: "",
      PinnedAuthorizeEndpoint: ""
    });
    renderProviders(view);
    setDirty(true);
  });
  view.querySelector("#btnAddRoleMapping").addEventListener("click", function() {
    if (!cfg) return;
    cfg.RoleMappings = collectRoleMappings(view);
    cfg.RoleMappings.push({
      RoleName: "",
      ProviderFilter: "",
      IsAdmin: false,
      EnableAllLibraries: false,
      LibraryIds: [],
      LibraryNames: [],
      EnableLiveTv: false,
      EnableLiveTvManagement: false,
      EnableMediaPlayback: true,
      EnableRemoteAccess: true,
      EnableTranscoding: true,
      EnableContentDeletion: false,
      EnableCollectionManagement: false,
      EnableSubtitleManagement: false,
      MaxParentalRatingName: "",
      MaxParentalRating: null
    });
    renderRoleMappings(view);
    setDirty(true);
  });
  view.querySelector("#btnSave").addEventListener("click", function() {
    if (!cfg) return;
    var idCounts = {};
    var duplicateIds = [];
    view.querySelectorAll("#providerList .oidc-item-card").forEach(function(card, idx) {
      var id = (gval(view, "prov_id_" + idx) || "").trim().toLowerCase();
      if (!id) return;
      idCounts[id] = (idCounts[id] || 0) + 1;
      if (idCounts[id] === 2) duplicateIds.push(id);
    });
    if (duplicateIds.length > 0) {
      Dashboard.alert({
        title: STRINGS.saveFlow.duplicateIdTitle,
        message: STRINGS.saveFlow.duplicateIdMessagePrefix + duplicateIds.join(", ") + STRINGS.saveFlow.duplicateIdMessageSuffix
      });
      return;
    }
    var unverified = [];
    view.querySelectorAll("#providerList .oidc-item-card").forEach(function(card, idx) {
      var issuerEl = view.querySelector("#prov_pinnedissuer_" + idx);
      if (!issuerEl) return;
      var verified = issuerEl.dataset.verified || "";
      if (verified && issuerEl.value !== verified) {
        unverified.push(gval(view, "prov_name_" + idx) || gval(view, "prov_id_" + idx) || "#" + (idx + 1));
      }
    });
    if (unverified.length > 0) {
      Dashboard.alert({
        title: STRINGS.saveFlow.issuerChangedTitle,
        message: STRINGS.saveFlow.issuerChangedMessagePrefix + unverified.join(", ") + STRINGS.saveFlow.issuerChangedMessageSuffix
      });
      return;
    }
    var unpinned = (cfg.Providers || []).filter(function(p, idx) {
      var el2 = view.querySelector("#prov_pinnedissuer_" + idx);
      return p.Enabled !== false && !(el2 && el2.dataset.verified);
    });
    if (unpinned.length > 0) {
      var names = unpinned.map(function(p) {
        return p.DisplayName || p.ProviderId;
      }).join(", ");
      if (!window.confirm(STRINGS.saveFlow.unpinnedConfirmPrefix + names + STRINGS.saveFlow.unpinnedConfirmSuffix)) {
        return;
      }
    }
    Dashboard.showLoadingMsg();
    cfg.Providers = collectProviders(view);
    cfg.RoleMappings = collectRoleMappings(view);
    cfg.DefaultRoleName = gval(view, "defaultRoleName");
    cfg.AutoCreateUsers = gchk(view, "autoCreateUsers");
    cfg.MigrateLocalUsers = gchk(view, "migrateLocalUsers");
    cfg.BlockPrivateNetworkAuthorities = gchk(view, "blockPrivateNetworkAuthorities");
    cfg.AllowedGroups = textToList(gval(view, "allowedGroups"));
    cfg.RequireVerifiedEmail = gchk(view, "requireVerifiedEmail");
    cfg.AllowedEmailDomains = textToList(gval(view, "allowedEmailDomains"));
    cfg.AllowedEmails = textToList(gval(view, "allowedEmails"));
    cfg.LinkExistingUsersByEmail = gchk(view, "linkExistingUsersByEmail");
    cfg.ManageUserPolicy = gchk(view, "manageUserPolicy");
    cfg.EnableLibraryAccessManagement = gchk(view, "enableLibraryAccessManagement");
    cfg.ManageLoginButtonBranding = gchk(view, "manageLoginButtonBranding");
    cfg.HideManualLogin = gchk(view, "hideManualLogin");
    cfg.LoginTitle = gval(view, "loginTitle") || STRINGS.loginPage.defaultLoginTitle;
    cfg.LoginSubtitle = gval(view, "loginSubtitle") || "";
    cfg.ServerBaseUrl = gval(view, "serverBaseUrl") || "";
    ApiClient.updatePluginConfiguration(pluginId, cfg).then(function(result) {
      Dashboard.processPluginConfigurationUpdateResult(result);
      return syncBranding(view);
    }).then(function() {
      loadBrandingSnippet(view);
      setDirty(false);
      Dashboard.hideLoadingMsg();
    }).catch(function(err) {
      Dashboard.hideLoadingMsg();
      Dashboard.alert(STRINGS.saveFlow.saveFailedPrefix + (err.message || err));
    });
  });
  view.querySelector("#providerList").addEventListener("click", function(e) {
    var copyBtn = e.target.closest("[data-copy]");
    if (copyBtn) {
      copyToClipboard(view, copyBtn.getAttribute("data-copy"), copyBtn);
      return;
    }
    var btn = e.target.closest("[data-action]");
    if (!btn) return;
    var idx = parseInt(btn.getAttribute("data-idx"));
    if (btn.getAttribute("data-action") === "remove-provider") {
      cfg.Providers = collectProviders(view);
      cfg.Providers.splice(idx, 1);
      renderProviders(view);
      setDirty(true);
    } else if (btn.getAttribute("data-action") === "move-provider") {
      var j = idx + parseInt(btn.getAttribute("data-dir"));
      if (j < 0 || j >= cfg.Providers.length) return;
      cfg.Providers = collectProviders(view);
      var moved = cfg.Providers.splice(idx, 1)[0];
      cfg.Providers.splice(j, 0, moved);
      renderProviders(view);
      setDirty(true);
    } else if (btn.getAttribute("data-action") === "test-provider") {
      testProvider(view, idx);
    } else if (btn.getAttribute("data-action") === "reset-color") {
      var el2 = view.querySelector("#prov_color_" + idx);
      if (el2) el2.value = DEFAULT_BUTTON_COLOR;
      setDirty(true);
    }
  });
  view.querySelector("#providerList").addEventListener("change", function(e) {
    var t = e.target;
    if (!t || !t.id) return;
    if (t.id.indexOf("prov_preset_") === 0 && t.value) {
      var pidx = t.id.slice("prov_preset_".length);
      var preset = PROVIDER_PRESETS[t.value];
      t.value = "";
      if (!preset) return;
      sval(view, "prov_roleclaim_" + pidx, preset.roleClaim);
      sval(view, "prov_userclaim_" + pidx, preset.usernameClaim);
      sval(view, "prov_scopes_" + pidx, preset.scopes);
      var iconSel = view.querySelector("#prov_icon_" + pidx);
      if (iconSel) {
        iconSel.value = preset.icon || "none";
        iconSel.dispatchEvent(new Event("change", { bubbles: true }));
      }
      setDirty(true);
      return;
    }
    if (t.id.indexOf("prov_icon_") === 0 && t.tagName === "SELECT") {
      var idx = t.id.slice("prov_icon_".length);
      var custom = t.value === "custom";
      var file = view.querySelector("#prov_icon_file_" + idx);
      if (file) file.classList.toggle("oidc-hidden", !custom);
      if (!custom) {
        var svgCleared = view.querySelector("#prov_icon_svg_" + idx);
        if (svgCleared) svgCleared.value = "";
        var statusCleared = view.querySelector('[data-icon-status="' + idx + '"]');
        if (statusCleared) statusCleared.textContent = "";
      }
    } else if (t.id.indexOf("prov_icon_file_") === 0 && t.files && t.files[0]) {
      var fidx = t.id.slice("prov_icon_file_".length);
      var f = t.files[0];
      var reader = new FileReader();
      reader.onload = function() {
        var hidden = view.querySelector("#prov_icon_svg_" + fidx);
        if (hidden) hidden.value = String(reader.result || "").trim();
        var status = view.querySelector('[data-icon-status="' + fidx + '"]');
        if (status) status.textContent = STRINGS.provider.iconCustomSetWithNamePrefix + f.name + STRINGS.provider.iconCustomSetWithNameSuffix;
      };
      if (/svg/i.test(f.type) || /\.svg$/i.test(f.name)) {
        reader.readAsText(f);
      } else {
        reader.readAsDataURL(f);
      }
    }
  });
  view.querySelector("#roleMappingList").addEventListener("click", function(e) {
    if (e.target.classList.contains("remove")) {
      e.target.parentElement.remove();
      setDirty(true);
      return;
    }
    var btn = e.target.closest("[data-action]");
    if (!btn) return;
    var idx = parseInt(btn.getAttribute("data-idx"));
    if (btn.getAttribute("data-action") === "remove-role") {
      cfg.RoleMappings = collectRoleMappings(view);
      cfg.RoleMappings.splice(idx, 1);
      renderRoleMappings(view);
      setDirty(true);
    } else if (btn.getAttribute("data-action") === "add-lib") {
      var sel = view.querySelector("#role_libadd_" + idx);
      if (!sel || !sel.value) return;
      var cont = view.querySelector("#role_libs_" + idx);
      var chips = cont.querySelectorAll(".oidc-library-chip");
      for (var i = 0; i < chips.length; i++) {
        if (chips[i].getAttribute("data-lib-id") === sel.value) return;
      }
      addLibChip(cont, sel.value);
      sel.value = "";
      setDirty(true);
    }
  });
}
export {
  index_default as default
};
