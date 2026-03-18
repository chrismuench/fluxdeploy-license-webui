// ECDSA P-256 SHA-256 signing/verification via Web Crypto API
// Compatible with .NET's ECDsa.SignData(data, SHA256, IeeeP1363FixedFieldConcatenation)

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

const ecdsaAlgorithm = { name: "ECDSA", namedCurve: "P-256" };
const ecdsaSignParams = { name: "ECDSA", hash: "SHA-256" };

let _cachedPrivateKey = null;
let _cachedPrivateKeyHash = null;
let _cachedPublicKey = null;
let _cachedPublicKeyHash = null;

async function hashPem(pem) {
    const data = new TextEncoder().encode(pem);
    const hash = await crypto.subtle.digest('SHA-256', data);
    return arrayBufferToBase64(hash);
}

window.ecdsaCrypto = {
    importPrivateKey: async function (privateKeyPem) {
        const h = await hashPem(privateKeyPem);
        if (h === _cachedPrivateKeyHash && _cachedPrivateKey) return true;
        const keyData = pemToArrayBuffer(privateKeyPem);
        _cachedPrivateKey = await crypto.subtle.importKey("pkcs8", keyData, ecdsaAlgorithm, false, ["sign"]);
        _cachedPrivateKeyHash = h;
        return true;
    },

    importPublicKey: async function (publicKeyPem) {
        const h = await hashPem(publicKeyPem);
        if (h === _cachedPublicKeyHash && _cachedPublicKey) return true;
        const keyData = pemToArrayBuffer(publicKeyPem);
        _cachedPublicKey = await crypto.subtle.importKey("spki", keyData, ecdsaAlgorithm, false, ["verify"]);
        _cachedPublicKeyHash = h;
        return true;
    },

    // Sign data. Returns base64-encoded signature (IEEE P1363 format = 64 bytes for P-256).
    sign: async function (dataBase64) {
        if (!_cachedPrivateKey) throw new Error("Private key not imported");
        const data = Uint8Array.from(atob(dataBase64), c => c.charCodeAt(0));
        const signature = await crypto.subtle.sign(ecdsaSignParams, _cachedPrivateKey, data);
        return arrayBufferToBase64(signature);
    },

    // Verify signature.
    verify: async function (dataBase64, signatureBase64) {
        if (!_cachedPublicKey) throw new Error("Public key not imported");
        const data = Uint8Array.from(atob(dataBase64), c => c.charCodeAt(0));
        const signature = Uint8Array.from(atob(signatureBase64), c => c.charCodeAt(0));
        return await crypto.subtle.verify(ecdsaSignParams, _cachedPublicKey, signature, data);
    },

    clearKeys: function () {
        _cachedPrivateKey = null;
        _cachedPrivateKeyHash = null;
        _cachedPublicKey = null;
        _cachedPublicKeyHash = null;
    },

    generateKeyPair: async function () {
        const keyPair = await crypto.subtle.generateKey(ecdsaAlgorithm, true, ["sign", "verify"]);
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
