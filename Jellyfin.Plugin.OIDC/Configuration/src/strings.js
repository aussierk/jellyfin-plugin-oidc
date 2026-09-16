
export var STRINGS = {
    pageHeading: 'OIDC RBAC Configuration',

    common: {
        removeBtn: 'Remove'
    },

    tabs: {
        general: 'General',
        providers: 'Providers',
        roles: 'Role Mappings'
    },

    general: {
        autoCreateUsers: 'Auto-Create Users on First OIDC Login',
        migrateLocalUsers: 'Migrate Local Users to SSO on First OIDC Login',
        migrateLocalUsersDesc: 'Existing local accounts start signing in via SSO instead of their local password.',
        blockPrivateNetwork: 'Block RFC1918/ULA Authorities',
        blockPrivateNetworkDesc: 'Off by default since self-hosted identity providers often use them.',
        serverBaseUrlLabel: 'Server Base URL (Override)',
        serverBaseUrlPlaceholder: 'https://jellyfin.example.com',
        serverBaseUrlDesc: 'Overrides the auto-detected host used for OIDC redirect and logout URLs.'
    },

    access: {
        legend: 'Access',
        allowedGroupsLabel: 'Allowed Groups',
        allowedGroupsHint: '(one per line; matched against the role claim)',
        requireVerifiedEmailHtml: 'Require a Verified Email (<code>email_verified</code>)',
        emailAllowlistWarningHtml: '&#9888; <strong>Ignored</strong> while &ldquo;Require a Verified Email&rdquo; above is unchecked.',
        allowedEmailDomainsLabel: 'Allowed Email Domains',
        allowedEmailDomainsHint: '(one per line; subdomains match too)',
        allowedEmailsLabel: 'Allowed Emails',
        allowedEmailsHint: '(exact addresses)',
        linkExistingUsersByEmail: "Link Accounts by Verified Email When the Subject Doesn't Match"
    },

    loginPage: {
        legend: 'Login Page',
        manageLoginButtonBranding: 'Manage the Login Button in Branding Automatically',
        manageLoginButtonBrandingDescHtml: 'Syncs login buttons on every first-party client. Non-web apps sign in via Quick Connect.',
        statusLabel: 'Status:',
        manualInstallSummary: 'Manual Install (Copy/Paste)',
        manualInstallDesc: 'Paste the HTML into Login Disclaimer and the CSS into Custom CSS.',
        copyHtmlBtn: 'Copy Login Button HTML',
        copyCssBtn: 'Copy Login Button CSS',
        hideManualLogin: 'Hide the Username/Password Form on the Login Page',
        hideManualLoginDescHtml: 'Hides the local login form and <strong>Forgot Password</strong> link; Quick Connect still works.',
        loginSubtitleLabel: 'Login Instructions',
        loginSubtitleHint: '(optional)',
        loginSubtitlePlaceholder: 'e.g. On a TV, choose Use Quick Connect and enter the code',
        loginTitleLabel: 'Login Page Heading',
        defaultLoginTitle: 'Please Sign In'
    },

    providersTab: {
        addProviderBtn: '+ Add Provider',
        emptyState: "No providers configured yet, so users can't sign in with SSO until you add one."
    },

    rolesTab: {
        legend: 'RBAC Management',
        manageUserPolicy: 'Manage User Policy from Role Mappings',
        manageUserPolicyDesc: "Turn off to manage users' Jellyfin policy manually (every field below becomes inert)",
        enableLibraryAccessManagement: 'Manage Library Access from Role Mappings',
        enableLibraryAccessManagementHint: '(only while policy management is on)',
        fallbackRoleLabel: 'Default Role',
        fallbackRoleHint: "(applied when a user's claim matches no role below)",
        addRoleMappingBtn: '+ Add Role Mapping',
        inactiveHint: 'Policy management is off, role mappings and the default role above are not applied.',
        emptyState: 'No role mappings, users receive the default role that is set above.'
    },

    save: {
        saveBtn: 'Save'
    },

    provider: {
        newProviderName: 'New Provider',
        enabledLabel: 'Enabled',
        connectionTitle: 'Connection',
        connectionHint: 'provider id, endpoint, client credentials & logout',
        claimMappingTitle: 'Claim Mapping',
        claimMappingHint: 'role, username, display name & avatar',
        appearanceTitle: 'Appearance',
        appearanceHint: 'login button color & icon',
        securityTitle: 'Security',
        securityHint: 'token validation, network guards, email-linking trust',
        prefillLabelPrefix: 'Prefill for ',
        prefillHintHtml: '(claims, scopes &amp; icon; you&rsquo;ll still need to add the Issuer URL &amp; credentials)',
        prefillChooseOption: 'Select an Identity Provider',
        providerIdLabel: 'Provider ID',
        providerIdPlaceholder: 'Unique Identifier (e.g. keycloak)',
        displayNameLabel: 'Display Name',
        displayNamePlaceholder: 'Shown on Login Button',
        issuerUrlLabel: 'Issuer URL',
        issuerUrlPlaceholder: 'https://idp.example.com/realms/myrealm',
        issuerHintPrefix: 'Must match the issuer from discovery. ',
        issuerHintPinned: 'Pinned. Please re-run Test Connection after editing.',
        issuerHintUnpinned: 'Run Test Connection to pin it.',
        clientIdLabel: 'Client ID',
        clientSecretLabel: 'Client Secret',
        clientSecretPlaceholderPrefix: 'Or reference an env var unique to this provider, e.g. ${',
        clientSecretFileLabel: 'Client Secret File',
        clientSecretFilePlaceholder: 'Optional: path to a file unique to this provider (e.g. a mounted Docker/K8s secret). Overrides Client Secret above.',
        scopesLabel: 'Scopes',
        additionalParamsLabel: 'Additional Parameters',
        additionalParamsPlaceholder: 'key=value1&key2=value2 (extra /authorize params)',
        backchannelLogoutLabel: 'Back-Channel Logout URL',
        backchannelLogoutHintHtml: 'Register with your identity provider as <code>backchannel_logout_uri</code>.',
        copyBtn: 'Copy',
        roleClaimLabel: 'Role Claim Path',
        roleClaimPlaceholder: 'e.g. groups or realm_access.roles',
        usernameClaimLabel: 'Username Claim',
        displayNameClaimLabel: 'Display Name Claim',
        emailClaimLabel: 'Email Claim',
        pictureClaimLabel: 'Picture Claim',
        pictureClaimPlaceholder: 'e.g. picture',
        syncProfileImage: 'Sync Profile Image',
        syncDisplayName: 'Sync Display Name on Login',
        syncDisplayNameDescHtml: '<strong>Renames the Jellyfin account</strong> to match the claim on every login.',
        buttonColorLabel: 'Button Color',
        resetToDefaultBtn: 'Reset to Default',
        buttonIconLabel: 'Button Icon',
        iconNone: 'None',
        iconCustom: 'Custom Image',
        iconCustomSet: 'Custom Icon Set',
        iconCustomSetWithNamePrefix: 'Custom Icon Set (',
        iconCustomSetWithNameSuffix: ')',
        strictAccessValidation: 'Strict Access Token Validation',
        strictAccessValidationDesc: 'Validates JWT access tokens against JWKS (opaque tokens skip this). Uncheck if your identity provider signs tokens with a key not published in its JWKS.',
        allowLoopback: 'Allow Loopback Authority',
        allowLoopbackDesc: 'Blocked by default (127.0.0.1, ::1). Enable only if intentional.',
        allowLinkLocal: 'Allow Link-Local Authority',
        allowLinkLocalDesc: 'Blocked by default (169.254.x.x, fe80::). Enable only if intentional.',
        trustedEmailLink: 'Trusted for Email-Based Account Linking',
        trustedEmailLinkDesc: 'Only matters when email-linking is on. Enable only for an identity provider you fully control; it never links to an admin account.',
        moveUpTitle: 'Move up (changes login-button order)',
        moveDownTitle: 'Move down (changes login-button order)',
        testConnectionBtn: 'Test Connection'
    },

    presetLabels: {
        keycloak: 'Keycloak',
        authentik: 'Authentik',
        authelia: 'Authelia',
        entra: 'Microsoft Entra ID',
        google: 'Google Workspace',
        okta: 'Okta',
        auth0: 'Auth0'
    },

    iconLabels: {
        github: 'GitHub'
    },

    role: {
        newRoleName: 'New Role',
        roleNamePrefix: 'Role: ',
        adminBadge: 'Admin',
        allProviders: 'All Providers',
        allLibraries: 'All Libraries',
        noLibraryAccess: 'No Library Access',
        librarySingular: ' Library',
        libraryPlural: ' Libraries',
        roleNameLabel: 'Role Name',
        roleNamePlaceholder: 'Must match identity provider role claim value',
        providerFilterLabel: 'Provider Filter ',
        providerFilterHint: '(blank = all providers)',
        permissionsLabel: 'Permissions ',
        permissionsHint: '(Administrator grants everything below)',
        permissionsNote: 'Multiple matching roles combine permissions; the strictest parental rating wins.',
        administrator: 'Administrator',
        playbackGroup: 'Playback',
        transcoding: 'Transcoding',
        remoteAccess: 'Remote Access',
        liveTvGroup: 'Live TV',
        liveTvAccess: 'Access',
        liveTvRecordingManagement: 'Recording Management',
        contentManagementGroup: 'Content Management',
        collections: 'Collections',
        subtitles: 'Subtitles',
        deleteContent: 'Delete Content',
        libraryAccessTitle: 'Library Access',
        specificLibrariesLabel: 'Specific Libraries ',
        specificLibrariesHint: '(Only applies when "All libraries" is off)',
        selectLibraryOption: 'Select Library',
        addLibraryBtn: 'Add Library',
        maxParentalRatingLabel: 'Max Parental Rating ',
        maxParentalRatingHint: '(empty = unrestricted)',
        unrestrictedOption: 'Unrestricted',
        notDefinedSuffix: ' (not defined on this server)',
        customScorePrefix: 'Custom Score ',
        customScoreSuffix: ' (select again to refresh)',
        noneOption: 'None',
        notDefinedRoleSuffix: ' (not a defined role)'
    },

    testConnection: {
        issuerRequired: 'Issuer URL is required',
        testing: 'Testing...',
        okPrefix: 'OK, issuer ',
        scopeWarningPrefix: ' (warning: scopes not advertised: ',
        scopeWarningSuffix: ')',
        failedPrefix: 'Failed: ',
        dialogTitleOk: 'Provider OK',
        dialogTitleFailed: 'Provider Test Failed',
        unknownError: 'Unknown Error',
        networkError: 'Network Error',
        issuerLinePrefix: 'Issuer: ',
        authorizeLinePrefix: 'Authorize: ',
        tokenLinePrefix: 'Token: ',
        userInfoLinePrefix: 'UserInfo: ',
        scopeWarningBlockPrefix: 'Warning: these requested scopes are not in scopes_supported:  '
    },

    branding: {
        installed: 'Installed',
        notInstalled: 'Not Installed',
        removeConfirm: 'The plugin previously added an SSO login button to Branding (Login Disclaimer + Custom CSS). Remove it now? Cancel leaves it in place.'
    },

    saveFlow: {
        duplicateIdTitle: 'Duplicate Provider ID',
        duplicateIdMessagePrefix: 'Provider ID(s) used by more than one provider: ',
        duplicateIdMessageSuffix: '. Each provider must have a unique Provider ID.',
        issuerChangedTitle: 'Issuer URL changed',
        issuerChangedMessagePrefix: 'Provider(s) with an edited, unverified Issuer URL: ',
        issuerChangedMessageSuffix: '. Run Test Connection to re-pin before saving.',
        unpinnedConfirmPrefix: 'Provider(s) without endpoint pins: ',
        unpinnedConfirmSuffix: '. Endpoints will be trusted on first login. Run Test Connection first to avoid this. Save anyway?',
        saveFailedPrefix: 'Failed to Save: ',
        unsavedChanges: '● Unsaved changes'
    },

    copyBtnCopied: 'Copied'
};

// Applies every data-str[-html] attribute in `view` to its element's text, every
// data-str-placeholder to its `placeholder` attribute, and every data-str-label to its
// `label` attribute (read once by emby-input's own upgrade, so this must run before that
// upgrade happens). Resolves dot-paths against STRINGS (e.g. data-str="rolesTab.legend").
// Call once, before any other page wiring, so the DOM matches STRINGS before the rest of
// index.js reads/writes it.
var resolveCache = {};
function resolve(path) {
    if (Object.prototype.hasOwnProperty.call(resolveCache, path)) return resolveCache[path];
    var parts = path.split('.');
    var v = STRINGS;
    for (var i = 0; i < parts.length; i++) {
        v = v == null ? undefined : v[parts[i]];
    }
    if (v == null) console.warn('OIDC RBAC: no STRINGS entry for "' + path + '"');
    resolveCache[path] = v;
    return v;
}

// One combined attribute->apply table instead of four separate full-DOM scans.
var STR_ATTRS = [
    ['data-str', function (elm, v) { elm.textContent = v; }],
    ['data-str-html', function (elm, v) { elm.innerHTML = v; }],
    ['data-str-placeholder', function (elm, v) { elm.setAttribute('placeholder', v); }],
    ['data-str-label', function (elm, v) { elm.setAttribute('label', v); }]
];

export function applyStrings(view) {
    var selector = STR_ATTRS.map(function (pair) { return '[' + pair[0] + ']'; }).join(',');
    view.querySelectorAll(selector).forEach(function (elm) {
        STR_ATTRS.forEach(function (pair) {
            var attr = pair[0], apply = pair[1];
            if (!elm.hasAttribute(attr)) return;
            var v = resolve(elm.getAttribute(attr));
            if (v != null) apply(elm, v);
        });
    });
}
