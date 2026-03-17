// RSA PKCS1-v1_5 SHA-256 signing/verification via Web Crypto API
// Compatible with .NET's RSA.SignData(data, SHA256, Pkcs1)

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

const rsaAlgorithm = { name: "RSASSA-PKCS1-v1_5", hash: "SHA-256" };

window.rsaCrypto = {
    sign: async function (privateKeyPem, dataBase64) {
        const keyData = pemToArrayBuffer(privateKeyPem);
        const key = await crypto.subtle.importKey("pkcs8", keyData, rsaAlgorithm, false, ["sign"]);
        const data = Uint8Array.from(atob(dataBase64), c => c.charCodeAt(0));
        const signature = await crypto.subtle.sign("RSASSA-PKCS1-v1_5", key, data);
        return btoa(String.fromCharCode(...new Uint8Array(signature)));
    },

    verify: async function (publicKeyPem, dataBase64, signatureBase64) {
        const keyData = pemToArrayBuffer(publicKeyPem);
        const key = await crypto.subtle.importKey("spki", keyData, rsaAlgorithm, false, ["verify"]);
        const data = Uint8Array.from(atob(dataBase64), c => c.charCodeAt(0));
        const signature = Uint8Array.from(atob(signatureBase64), c => c.charCodeAt(0));
        return await crypto.subtle.verify("RSASSA-PKCS1-v1_5", key, signature, data);
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

function arrayBufferToPem(buffer, label) {
    const b64 = btoa(String.fromCharCode(...new Uint8Array(buffer)));
    const lines = b64.match(/.{1,64}/g) || [];
    return `-----BEGIN ${label}-----\n${lines.join('\n')}\n-----END ${label}-----`;
}

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
    window.location.href = document.baseURI;
};

window.readFileText = function (inputElementId) {
    return new Promise((resolve, reject) => {
        const input = document.getElementById(inputElementId);
        if (!input || !input.files || !input.files[0]) { resolve(null); return; }
        const reader = new FileReader();
        reader.onload = () => resolve(reader.result);
        reader.onerror = () => reject(reader.error);
        reader.readAsText(input.files[0]);
    });
};
