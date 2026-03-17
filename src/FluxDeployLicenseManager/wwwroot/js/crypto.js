// RSA PKCS1-v1_5 SHA-256 signing/verification via Web Crypto API
// Compatible with .NET's RSA.SignData(data, SHA256, Pkcs1)

"use strict";

function pemToArrayBuffer(pem) {
    const lines = pem.split('\n').filter(l => !l.startsWith('-----'));
    const b64 = lines.join('');
    const binary = atob(b64);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++) {
        bytes[i] = binary.charCodeAt(i);
    }
    return bytes.buffer;
}

// WEB-007: Safe ArrayBuffer to base64 conversion (no spread operator)
function arrayBufferToBase64(buffer) {
    const bytes = new Uint8Array(buffer);
    let binary = '';
    for (let i = 0; i < bytes.length; i++) {
        binary += String.fromCharCode(bytes[i]);
    }
    return btoa(binary);
}

function arrayBufferToPem(buffer, label) {
    const b64 = arrayBufferToBase64(buffer);
    const lines = b64.match(/.{1,64}/g) || [];
    return '-----BEGIN ' + label + '-----\n' + lines.join('\n') + '\n-----END ' + label + '-----';
}

const rsaAlgorithm = { name: "RSASSA-PKCS1-v1_5", hash: "SHA-256" };

// WEB-006: Cache imported CryptoKey objects to avoid re-transmitting PEM on every operation
let _cachedPrivateKey = null;
let _cachedPrivateKeyHash = null;
let _cachedPublicKey = null;
let _cachedPublicKeyHash = null;

// Simple hash for cache invalidation (not crypto - just for detecting PEM changes)
async function hashPem(pem) {
    const data = new TextEncoder().encode(pem);
    const hash = await crypto.subtle.digest('SHA-256', data);
    return arrayBufferToBase64(hash);
}

window.rsaCrypto = {
    // Import and cache a private key. Returns true on success.
    importPrivateKey: async function (privateKeyPem) {
        const h = await hashPem(privateKeyPem);
        if (h === _cachedPrivateKeyHash && _cachedPrivateKey) return true;
        const keyData = pemToArrayBuffer(privateKeyPem);
        _cachedPrivateKey = await crypto.subtle.importKey("pkcs8", keyData, rsaAlgorithm, false, ["sign"]);
        _cachedPrivateKeyHash = h;
        return true;
    },

    // Import and cache a public key. Returns true on success.
    importPublicKey: async function (publicKeyPem) {
        const h = await hashPem(publicKeyPem);
        if (h === _cachedPublicKeyHash && _cachedPublicKey) return true;
        const keyData = pemToArrayBuffer(publicKeyPem);
        _cachedPublicKey = await crypto.subtle.importKey("spki", keyData, rsaAlgorithm, false, ["verify"]);
        _cachedPublicKeyHash = h;
        return true;
    },

    // Sign data using the cached private key. importPrivateKey must be called first.
    sign: async function (dataBase64) {
        if (!_cachedPrivateKey) throw new Error("Private key not imported");
        const data = Uint8Array.from(atob(dataBase64), c => c.charCodeAt(0));
        const signature = await crypto.subtle.sign("RSASSA-PKCS1-v1_5", _cachedPrivateKey, data);
        return arrayBufferToBase64(signature);
    },

    // Verify signature using the cached public key. importPublicKey must be called first.
    verify: async function (dataBase64, signatureBase64) {
        if (!_cachedPublicKey) throw new Error("Public key not imported");
        const data = Uint8Array.from(atob(dataBase64), c => c.charCodeAt(0));
        const signature = Uint8Array.from(atob(signatureBase64), c => c.charCodeAt(0));
        return await crypto.subtle.verify("RSASSA-PKCS1-v1_5", _cachedPublicKey, signature, data);
    },

    // Clear cached keys (call on session end)
    clearKeys: function () {
        _cachedPrivateKey = null;
        _cachedPrivateKeyHash = null;
        _cachedPublicKey = null;
        _cachedPublicKeyHash = null;
    },

    generateKeyPair: async function () {
        const keyPair = await crypto.subtle.generateKey(
            { name: "RSASSA-PKCS1-v1_5", modulusLength: 4096, publicExponent: new Uint8Array([1, 0, 1]), hash: "SHA-256" },
            true, ["sign", "verify"]
        );
        const privBytes = await crypto.subtle.exportKey("pkcs8", keyPair.privateKey);
        const pubBytes = await crypto.subtle.exportKey("spki", keyPair.publicKey);
        return {
            privateKeyPem: arrayBufferToPem(privBytes, "PRIVATE KEY"),
            publicKeyPem: arrayBufferToPem(pubBytes, "PUBLIC KEY")
        };
    }
};

window.downloadTextFile = function (filename, content) {
    const blob = new Blob([content], { type: 'text/plain' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
};

// WEB-019: Use hardcoded relative path instead of document.baseURI
window.navigateToBase = function () {
    window.location.href = document.querySelector('base').getAttribute('href') || '/';
};

window.readFileText = function (inputElementId) {
    return new Promise(function (resolve, reject) {
        const input = document.getElementById(inputElementId);
        if (!input || !input.files || !input.files[0]) { resolve(null); return; }
        const reader = new FileReader();
        reader.onload = function () { resolve(reader.result); };
        reader.onerror = function () { reject(reader.error); };
        reader.readAsText(input.files[0]);
    });
};
