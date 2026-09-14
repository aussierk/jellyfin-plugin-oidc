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
  if (!src) return;
  if (typeof src.select === "function") src.select();
  if (navigator.clipboard) {
    navigator.clipboard.writeText(src.value).catch(function() {
    });
  } else {
    try {
      document.execCommand("copy");
    } catch (e) {
    }
  }
  var orig = btn.textContent;
  btn.textContent = "Copied";
  setTimeout(function() {
    btn.textContent = orig;
  }, 1200);
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
  if (s) s.textContent = v ? "\u25CF Unsaved changes" : "";
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
  if (el2) el2.textContent = installed ? "Installed" : "Not installed";
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
      action = window.confirm(
        "The plugin previously added an SSO login button to Branding (Login Disclaimer + Custom CSS).\n\nRemove it now? Cancel leaves it in place."
      ) ? "remove" : "none";
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
    { class: full ? "oidc-field full" : "oidc-field" },
    el("label", { for: id }, esc(label)) + el("input", {
      is: "emby-input",
      type,
      id,
      value: String(value || ""),
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
var ICON_LABELS = { auth0: "Auth0", github: "GitHub" };
var PROVIDER_PRESETS = {
  keycloak: { label: "Keycloak", roleClaim: "realm_access.roles", usernameClaim: "preferred_username", scopes: "openid profile email", icon: "keycloak" },
  authentik: { label: "Authentik", roleClaim: "groups", usernameClaim: "preferred_username", scopes: "openid profile email", icon: "authentik" },
  authelia: { label: "Authelia", roleClaim: "groups", usernameClaim: "preferred_username", scopes: "openid profile email groups", icon: "" },
  entra: { label: "Microsoft Entra ID", roleClaim: "roles", usernameClaim: "preferred_username", scopes: "openid profile email", icon: "microsoft" },
  google: { label: "Google Workspace", roleClaim: "groups", usernameClaim: "email", scopes: "openid profile email", icon: "google" },
  okta: { label: "Okta", roleClaim: "groups", usernameClaim: "preferred_username", scopes: "openid profile email groups", icon: "okta" },
  auth0: { label: "Auth0", roleClaim: "", usernameClaim: "nickname", scopes: "openid profile email", icon: "auth0" }
};
function presetField(idx) {
  var opts = el("option", { value: "" }, "- choose an IdP -");
  Object.keys(PROVIDER_PRESETS).forEach(function(k) {
    opts += el("option", { value: k }, esc(PROVIDER_PRESETS[k].label));
  });
  return el(
    "div",
    { class: "oidc-field full" },
    el("label", { for: "prov_preset_" + idx }, "Prefill for " + el("span", { class: "oidc-hint" }, "(sets claims / scopes / icon - you still enter the Issuer URL &amp; client credentials)")) + el("select", { id: "prov_preset_" + idx }, opts)
  );
}
function iconField(idx, cur) {
  var custom = iconIsCustom(cur);
  var opts = el("option", { value: "none", selected: !cur }, "None");
  ICON_KEYS.forEach(function(k) {
    var label = ICON_LABELS[k] || k.charAt(0).toUpperCase() + k.slice(1);
    opts += el("option", { value: k, selected: cur === k }, label);
  });
  opts += el("option", { value: "custom", selected: custom }, "Custom (image)");
  return el(
    "div",
    { class: "oidc-field full" },
    el("label", { for: "prov_icon_" + idx }, "Button Icon") + el("select", { is: "emby-select", id: "prov_icon_" + idx }, opts) + el("input", { type: "hidden", id: "prov_icon_svg_" + idx, value: custom ? cur : "" }) + el("input", {
      type: "file",
      id: "prov_icon_file_" + idx,
      accept: ".svg,.png,.jpg,.jpeg,.gif,.webp,image/svg+xml,image/png,image/jpeg,image/gif,image/webp",
      class: "oidc-mt-sm" + (custom ? "" : " oidc-hidden")
    }) + el("span", { class: "oidc-hint", "data-icon-status": idx }, custom && cur ? "Custom icon set" : "")
  );
}
function provGroup(title, hint, inner, open) {
  var head = esc(title) + (hint ? " " + el("span", { class: "oidc-hint" }, esc(hint)) : "");
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
    container.innerHTML = emptyState(
      "No providers configured yet - users can't sign in with SSO until you add one."
    );
    return;
  }
  cfg.Providers.forEach(function(p, idx) {
    var card = document.createElement("div");
    card.className = "oidc-card";
    var configured = !!(p.ProviderId && (p.PinnedIssuer || p.Authority) && p.ClientId);
    if (p.Enabled === false) card.className += " oidc-disabled";
    var connection = (configured ? "" : presetField(idx)) + fld("Provider ID", "text", "prov_id_" + idx, p.ProviderId, "Unique identifier (e.g. keycloak)") + fld("Display Name", "text", "prov_name_" + idx, p.DisplayName, "Shown on login button") + el(
      "div",
      { class: "oidc-field full" },
      el("label", { for: "prov_pinnedissuer_" + idx }, "Issuer URL") + el("input", {
        is: "emby-input",
        type: "text",
        id: "prov_pinnedissuer_" + idx,
        value: p.PinnedIssuer || p.Authority || "",
        placeholder: "https://idp.example.com/realms/myrealm",
        "data-verified": p.PinnedIssuer || "",
        autocomplete: "off",
        autocapitalize: "off",
        spellcheck: "false"
      }) + el("span", { class: "oidc-hint" }, "Must exactly match the issuer your IdP returns from discovery. " + (p.PinnedIssuer ? "This value is pinned - editing it requires re-running Test Connection before you can save." : "Run Test Connection to pin it."))
    ) + fld("Client ID", "text", "prov_clientid_" + idx, p.ClientId, "") + fld(
      "Client Secret",
      "password",
      "prov_secret_" + idx,
      p.ClientSecret,
      "Or reference an env var unique to THIS provider, e.g. ${" + envVarSuggestion(p) + "}"
    ) + fld(
      "Client Secret File",
      "text",
      "prov_secretfile_" + idx,
      p.ClientSecretFile,
      "Optional: path to a file unique to THIS provider (e.g. a mounted Docker/K8s secret) - overrides Client Secret above"
    ) + fld("Scopes", "text", "prov_scopes_" + idx, p.Scopes || "openid profile email", "") + fld("Additional Params", "text", "prov_params_" + idx, p.AdditionalParameters || "", "key=val&key2=val2 - extra query params added to the /authorize request", true) + (p.ProviderId ? el(
      "div",
      { class: "oidc-field full oidc-mt-md" },
      el("label", { class: "oidc-label-strong" }, "Back-channel logout URL " + el("span", { class: "oidc-hint" }, "- register as the client's <code>backchannel_logout_uri</code> so the IdP can revoke Jellyfin sessions")) + el(
        "div",
        { class: "oidc-inline-row oidc-mt-sm" },
        el("input", {
          is: "emby-input",
          type: "text",
          id: "prov_bclogout_" + idx,
          readonly: true,
          value: backchannelLogoutUrl(p, cfg.ServerBaseUrl),
          class: "oidc-mono-flex"
        }) + el("button", { type: "button", class: "oidc-btn-secondary", "data-copy": "prov_bclogout_" + idx }, "Copy")
      )
    ) : "");
    var claims = fld("Role Claim Path", "text", "prov_roleclaim_" + idx, p.RoleClaim || "groups", "e.g. groups or realm_access.roles") + fld("Username Claim", "text", "prov_userclaim_" + idx, p.UsernameClaim || "preferred_username", "") + fld("Display Name Claim", "text", "prov_displayclaim_" + idx, p.DisplayNameClaim || "name", "") + fld("Email Claim", "text", "prov_emailclaim_" + idx, p.EmailClaim || "email", "") + fld("Picture Claim", "text", "prov_pictureclaim_" + idx, p.PictureClaim || "picture", "e.g. picture") + el(
      "div",
      { class: "oidc-field full" },
      el(
        "label",
        null,
        el("input", { type: "checkbox", id: "prov_syncimage_" + idx, is: "emby-checkbox", checked: p.SyncProfileImage !== false }) + " " + el("span", null, "Sync profile image")
      )
    ) + el(
      "div",
      { class: "oidc-field full" },
      el(
        "label",
        null,
        el("input", { type: "checkbox", id: "prov_syncdisplay_" + idx, is: "emby-checkbox", checked: p.SyncDisplayName === true }) + " " + el("span", null, "Sync display name on login")
      ) + el("span", { class: "oidc-hint oidc-ml-lg" }, "This <strong>renames the Jellyfin account</strong> to match the Display Name Claim on every login.")
    );
    var appearance = el(
      "div",
      { class: "oidc-field" },
      el("label", { for: "prov_color_" + idx }, "Button Color") + el(
        "div",
        { class: "oidc-inline-row" },
        el("input", { type: "color", id: "prov_color_" + idx, value: p.ButtonColor || DEFAULT_BUTTON_COLOR }) + el("button", { type: "button", class: "oidc-btn-secondary", "data-action": "reset-color", "data-idx": idx }, "Reset to default")
      )
    ) + iconField(idx, p.ButtonIcon || "");
    var security = el(
      "div",
      { class: "oidc-field full" },
      el(
        "label",
        null,
        el("input", { type: "checkbox", id: "prov_strict_access_" + idx, is: "emby-checkbox", checked: p.StrictAccessTokenValidation !== false }) + " " + el("span", null, "Strict access token validation")
      ) + el("span", { class: "oidc-hint oidc-ml-lg" }, "Validates JWT access tokens against the JWKS endpoint; opaque tokens (Google, default Authelia) are skipped automatically. Uncheck if your IdP signs with a different key.")
    ) + el(
      "div",
      { class: "oidc-field full" },
      el(
        "label",
        null,
        el("input", { type: "checkbox", id: "prov_allow_loopback_" + idx, is: "emby-checkbox", checked: p.AllowLoopbackAuthority === true }) + " " + el("span", null, "Allow loopback Authority")
      ) + el("span", { class: "oidc-hint oidc-ml-lg" }, "Loopback Authorities (127.0.0.1, ::1) are blocked by default. Enable only if your IdP is intentionally hosted there.")
    ) + el(
      "div",
      { class: "oidc-field full" },
      el(
        "label",
        null,
        el("input", { type: "checkbox", id: "prov_allow_linklocal_" + idx, is: "emby-checkbox", checked: p.AllowLinkLocalAuthority === true }) + " " + el("span", null, "Allow link-local Authority")
      ) + el("span", { class: "oidc-hint oidc-ml-lg" }, "Link-local Authorities (169.254.x.x, fe80::) are blocked by default. Enable only if your IdP is intentionally hosted there.")
    ) + el(
      "div",
      { class: "oidc-field full" },
      el(
        "label",
        null,
        el("input", { type: "checkbox", id: "prov_trusted_email_link_" + idx, is: "emby-checkbox", checked: p.TrustedForEmailLinking === true }) + " " + el("span", null, "Trusted for email-based account linking")
      ) + el("span", { class: "oidc-hint oidc-ml-lg" }, 'Used only when "Link existing users by verified email" is on. Enable only for an IdP you fully control - its verified emails will link logins to existing accounts (never to an admin).')
    ) + el("input", { type: "hidden", id: "prov_discovery_" + idx, value: p.Authority || "" }) + el("input", { type: "hidden", id: "prov_pinnedauthority_" + idx, value: p.PinnedAuthority || "" }) + el("input", { type: "hidden", id: "prov_pinnedtoken_" + idx, value: p.PinnedTokenEndpoint || "" }) + el("input", { type: "hidden", id: "prov_pinnedjwks_" + idx, value: p.PinnedJwksUri || "" }) + el("input", { type: "hidden", id: "prov_pinneduserinfo_" + idx, value: p.PinnedUserInfoEndpoint || "" }) + el("input", { type: "hidden", id: "prov_pinnedauthorize_" + idx, value: p.PinnedAuthorizeEndpoint || "" }) + el(
      "div",
      { class: "oidc-hidden", "data-pin-status": idx },
      p.PinnedIssuer ? "Pinned via Test Connection - token endpoint, JWKS URI &amp; userinfo endpoint are locked to the values returned for this issuer." : "Not yet pinned - endpoints will be trusted on first login (TOFU) unless you run Test Connection first."
    );
    var host = authorityHost(p.PinnedIssuer || p.Authority);
    card.innerHTML = el(
      "div",
      { class: "oidc-card-head" },
      el("h4", null, esc(p.DisplayName || "New Provider")) + (host ? el("span", { class: "oidc-card-sub" }, esc(host)) : "") + el(
        "label",
        { class: "oidc-enable-toggle" },
        el("span", null, "Enabled") + el("input", { type: "checkbox", id: "prov_enabled_" + idx, checked: p.Enabled !== false })
      )
    ) + provGroup("Connection", "provider id, endpoint, client credentials & logout", connection, !configured) + provGroup("Claim mapping", "role, username, display name & avatar", claims, false) + provGroup("Appearance", "login button colour & icon", appearance, false) + provGroup("Security", "token validation, network guards, email-linking trust", security, false) + el(
      "div",
      { class: "oidc-row-actions" },
      el("button", {
        type: "button",
        class: "oidc-btn-secondary oidc-btn-icon",
        "data-action": "move-provider",
        "data-dir": "-1",
        "data-idx": idx,
        title: "Move up (changes login-button order)",
        disabled: idx === 0
      }, "&#8593;") + el("button", {
        type: "button",
        class: "oidc-btn-secondary oidc-btn-icon",
        "data-action": "move-provider",
        "data-dir": "1",
        "data-idx": idx,
        title: "Move down (changes login-button order)",
        disabled: idx === cfg.Providers.length - 1
      }, "&#8595;") + el("button", { type: "button", class: "oidc-btn-secondary", "data-action": "test-provider", "data-idx": idx }, "Test Connection") + el("button", { type: "button", class: "oidc-btn-remove", "data-action": "remove-provider", "data-idx": idx }, "Remove") + el("span", { class: "oidc-test-result", "data-idx": idx })
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
  view.querySelectorAll("#providerList .oidc-card").forEach(function(card, idx) {
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
  var opts = '<option value="">- Unrestricted -</option>';
  var known = false;
  ratings.forEach(function(r) {
    var sel = r.Name.toLowerCase() === selName.toLowerCase() ? " selected" : "";
    if (sel) {
      known = true;
    }
    opts += '<option value="' + esc(r.Name) + '"' + sel + ">" + esc(r.Name) + "</option>";
  });
  if (selName && !known) {
    opts += '<option value="' + esc(selName) + '" selected>' + esc(selName) + " (not defined on this server)</option>";
  } else if (!selName && legacy != null) {
    opts += '<option value="__legacy__" selected disabled>Custom score ' + legacy + " (re-pick to update)</option>";
  }
  return opts;
}
function renderDefaultRoleOptions(view) {
  var sel = view.querySelector("#defaultRoleName");
  if (!sel) return;
  var current = sel.value || cfg.DefaultRoleName || "";
  var source = view.querySelector("#roleMappingList .oidc-card") ? collectRoleMappings(view).map(function(m) {
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
  var opts = '<option value="">- none -</option>';
  if (current && names.every(function(x) {
    return x.toLowerCase() !== current.toLowerCase();
  })) {
    opts += '<option value="' + esc(current) + '">' + esc(current) + " (not a defined role)</option>";
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
    container.innerHTML = emptyState(
      'No role mappings - signed-in users get the fallback role selected above, or no extra permissions if that is "- none -".'
    );
    renderDefaultRoleOptions(view);
    return;
  }
  cfg.RoleMappings.forEach(function(m, idx) {
    var card = document.createElement("details");
    card.className = "oidc-card oidc-role";
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
    var provOpts = el("option", { value: "", selected: !m.ProviderFilter }, "All providers (global)") + (cfg.Providers || []).map(function(p) {
      return el(
        "option",
        { value: p.ProviderId, selected: m.ProviderFilter === p.ProviderId },
        esc(p.DisplayName || p.ProviderId)
      );
    }).join("");
    var provLabel = m.ProviderFilter ? ((cfg.Providers || []).find(function(p) {
      return p.ProviderId === m.ProviderFilter;
    }) || {}).DisplayName || m.ProviderFilter : "all providers";
    var libLabel = m.EnableAllLibraries ? "all libraries" : selectedLibs.length ? selectedLibs.length + (selectedLibs.length === 1 ? " library" : " libraries") : "no library access";
    var scopeParts = [provLabel, libLabel];
    card.innerHTML = el(
      "summary",
      { class: "oidc-role-summary" },
      el("h4", null, "Role: " + esc(m.RoleName || "New Role")) + (m.IsAdmin ? el("span", { class: "oidc-badge" }, "Admin") : "") + el("span", { class: "oidc-role-scope" }, esc(scopeParts.join("  \xB7  ")))
    ) + fld("Role Name", "text", "role_name_" + idx, m.RoleName, "Must match IdP role claim value", true) + el(
      "div",
      { class: "oidc-field full oidc-mb-md" },
      el("label", null, "Provider Filter " + el("span", { class: "oidc-hint" }, "(restrict to one provider - leave blank to apply to all)")) + el("select", { is: "emby-select", id: "role_provfilter_" + idx }, provOpts)
    ) + el(
      "div",
      { class: "oidc-field full oidc-mt-sm" },
      el("label", null, "Permissions " + el("span", { class: "oidc-hint" }, "(Administrator grants everything below)")) + el("p", { class: "oidc-hint oidc-hint-tight" }, "When a user matches several roles, all their permissions are combined and the strictest parental rating wins.") + el("div", { class: "oidc-checkbox-row oidc-mt-xs" }, chk("role_admin_" + idx, "Administrator", m.IsAdmin)) + permGroup(
        "Playback",
        chk("role_playback_" + idx, "Playback", m.EnableMediaPlayback !== false) + chk("role_transcode_" + idx, "Transcoding", m.EnableTranscoding !== false) + chk("role_remote_" + idx, "Remote Access", m.EnableRemoteAccess !== false)
      ) + permGroup(
        "Live TV",
        chk("role_livetv_" + idx, "Access", m.EnableLiveTv) + chk("role_livetvmgmt_" + idx, "Recording management", m.EnableLiveTvManagement)
      ) + permGroup(
        "Content management",
        chk("role_collections_" + idx, "Collections", m.EnableCollectionManagement) + chk("role_subtitles_" + idx, "Subtitles", m.EnableSubtitleManagement) + chk("role_delete_" + idx, "Delete content", m.EnableContentDeletion)
      )
    ) + el(
      "div",
      { class: "oidc-field full oidc-mt-md" },
      el("div", { class: "oidc-perm-title" }, "Library access") + el("div", { class: "oidc-checkbox-row" }, chk("role_alllibs_" + idx, "All libraries", m.EnableAllLibraries)) + el("label", { class: "oidc-mt-sm2" }, "Specific libraries " + el("span", { class: "oidc-hint" }, '(used when "All libraries" is off)')) + el("select", { is: "emby-select", id: "role_libadd_" + idx }, el("option", { value: "" }, "-- Select library --") + libOpts) + el("button", { type: "button", class: "oidc-btn-secondary oidc-mt-sm oidc-w-fit", "data-action": "add-lib", "data-idx": idx }, "Add Library") + el("div", { id: "role_libs_" + idx, class: "oidc-library-list" })
    ) + el(
      "div",
      { class: "oidc-field oidc-mt-md" },
      el("label", null, "Max Parental Rating " + el("span", { class: "oidc-hint" }, "(empty = unrestricted; strictest wins when several roles match)")) + el("select", { is: "emby-select", id: "role_maxrating_" + idx }, ratingOptions(m))
    ) + el(
      "div",
      { class: "oidc-mt-md" },
      el("button", { type: "button", class: "oidc-btn-remove", "data-action": "remove-role", "data-idx": idx }, "Remove")
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
  view.querySelectorAll("#roleMappingList .oidc-card").forEach(function(card, idx) {
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
  var hint = view.querySelector("#roleMappingsInactiveHint");
  [list, fallback, addBtn].forEach(function(node) {
    if (node) node.classList.toggle("oidc-dimmed", !managed);
  });
  if (fallback) fallback.disabled = !managed;
  if (addBtn) addBtn.disabled = !managed;
  if (hint) hint.hidden = managed;
}
function updateEmailAllowlistUi(view) {
  var active = view.querySelector("#requireVerifiedEmail").checked;
  var fields = view.querySelector("#emailAllowlistFields");
  if (!fields) return;
  fields.querySelectorAll(".oidc-field").forEach(function(field) {
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
    setTestStatus(resultEl, "error", "Issuer URL is required");
    return;
  }
  setTestStatus(resultEl, "dim", "Testing...");
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
      var statusEl = view.querySelector('[data-pin-status="' + idx + '"]');
      if (statusEl) {
        statusEl.textContent = "Pinned via Test Connection - token endpoint, JWKS URI & userinfo endpoint are locked to the values returned for this issuer.";
      }
      var sec = issuerEl && issuerEl.closest("details.oidc-section");
      if (sec) sec.open = true;
      setDirty(true);
      var msg = "OK - issuer " + result.Issuer;
      var hasScopeWarning = result.UnsupportedRequestedScopes && result.UnsupportedRequestedScopes.length > 0;
      if (hasScopeWarning) {
        msg += " (warning: scopes not advertised: " + result.UnsupportedRequestedScopes.join(", ") + ")";
      }
      setTestStatus(resultEl, hasScopeWarning ? "warn" : "ok", msg);
      Dashboard.alert({
        title: "Provider OK",
        message: "Issuer: " + result.Issuer + "\nAuthorize: " + result.AuthorizationEndpoint + "\nToken: " + result.TokenEndpoint + "\n" + (result.UserInfoEndpoint ? "UserInfo: " + result.UserInfoEndpoint + "\n" : "") + (result.UnsupportedRequestedScopes && result.UnsupportedRequestedScopes.length > 0 ? "\nWarning: these requested scopes are not in scopes_supported:\n  " + result.UnsupportedRequestedScopes.join(", ") : "")
      });
    } else {
      setTestStatus(resultEl, "error", "Failed: " + result.Error);
      Dashboard.alert({ title: "Provider test failed", message: result.Error || "Unknown error" });
    }
  }).catch(function(err) {
    var msg = err && (err.statusText || err.message) || "Network error";
    setTestStatus(resultEl, "error", "Failed: " + msg);
    Dashboard.alert({ title: "Provider test failed", message: msg });
  });
}

// Jellyfin.Plugin.OIDC/Configuration/src/index.js
function autogrowTextarea(el2) {
  if (!el2) return;
  el2.style.height = "auto";
  el2.style.height = el2.scrollHeight + "px";
}
function index_default(view) {
  setDirtyView(view);
  window.addEventListener("beforeunload", beforeUnloadGuard);
  view.addEventListener("input", function(e) {
    setDirty(true);
    if (e.target && e.target.classList && e.target.classList.contains("oidc-autogrow")) autogrowTextarea(e.target);
    if (e.target && e.target.id && e.target.id.indexOf("role_name_") === 0) renderDefaultRoleOptions(view);
    if (e.target && e.target.id && e.target.id.indexOf("prov_pinnedissuer_") === 0) {
      var idx = e.target.id.slice("prov_pinnedissuer_".length);
      var verified = e.target.dataset.verified || "";
      var statusEl = view.querySelector('[data-pin-status="' + idx + '"]');
      if (statusEl && verified) {
        statusEl.textContent = e.target.value === verified ? "Pinned via Test Connection - token endpoint, JWKS URI & userinfo endpoint are locked to the values returned for this issuer." : "Issuer URL changed - run Test Connection to re-pin before you can save.";
      }
    }
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
      sval(view, "loginTitle", cfg.LoginTitle || "Please sign in");
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
  view.querySelectorAll(".oidc-tab").forEach(function(tab) {
    tab.addEventListener("click", function() {
      view.querySelectorAll(".oidc-tab").forEach(function(t) {
        t.classList.remove("is-active");
        t.setAttribute("aria-selected", "false");
      });
      view.querySelectorAll(".oidc-tab-content").forEach(function(c) {
        c.classList.add("oidc-hidden");
      });
      this.classList.add("is-active");
      this.setAttribute("aria-selected", "true");
      view.querySelector("#tab-" + this.getAttribute("data-tab")).classList.remove("oidc-hidden");
    });
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
      DisplayName: "New Provider",
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
    view.querySelectorAll("#providerList .oidc-card").forEach(function(card, idx) {
      var id = (gval(view, "prov_id_" + idx) || "").trim().toLowerCase();
      if (!id) return;
      idCounts[id] = (idCounts[id] || 0) + 1;
      if (idCounts[id] === 2) duplicateIds.push(id);
    });
    if (duplicateIds.length > 0) {
      Dashboard.alert({
        title: "Duplicate Provider ID",
        message: "Provider ID(s) used by more than one provider: " + duplicateIds.join(", ") + ".\n\nEach provider must have a unique Provider ID."
      });
      return;
    }
    var unverified = [];
    view.querySelectorAll("#providerList .oidc-card").forEach(function(card, idx) {
      var issuerEl = view.querySelector("#prov_pinnedissuer_" + idx);
      if (!issuerEl) return;
      var verified = issuerEl.dataset.verified || "";
      if (verified && issuerEl.value !== verified) {
        unverified.push(gval(view, "prov_name_" + idx) || gval(view, "prov_id_" + idx) || "#" + (idx + 1));
      }
    });
    if (unverified.length > 0) {
      Dashboard.alert({
        title: "Issuer URL changed",
        message: "Provider(s) with an edited, unverified Issuer URL: " + unverified.join(", ") + ".\n\nRun Test Connection to re-pin before saving."
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
      if (!window.confirm("Provider(s) without endpoint pins: " + names + ".\n\nEndpoints will be trusted on first login (TOFU). Run Test Connection to eliminate this window.\n\nSave anyway?")) {
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
    cfg.LoginTitle = gval(view, "loginTitle") || "Please sign in";
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
      Dashboard.alert("Failed to save: " + (err.message || err));
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
        if (status) status.textContent = "Custom icon set (" + f.name + ")";
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
