// IndexedDB cache for RA v2 support assets (MIX, etc.). Keys include a version string from C# (RaV2BrowserAssetCacheVersion).
window.openraAssetCache = (function () {
    const DB_NAME = 'openra-ra-v2-assets';
    const DB_VERSION = 1;
    const STORE_NAME = 'blobs';

    function openDb() {
        return new Promise((resolve, reject) => {
            const req = indexedDB.open(DB_NAME, DB_VERSION);
            req.onerror = () => reject(req.error);
            req.onsuccess = () => resolve(req.result);
            req.onupgradeneeded = (e) => {
                const db = e.target.result;
                if (!db.objectStoreNames.contains(STORE_NAME))
                    db.createObjectStore(STORE_NAME);
            };
        });
    }

    let dbPromise = null;
    function getDb() {
        if (!dbPromise)
            dbPromise = openDb();
        return dbPromise;
    }

    return {
        tryGet: async function (cacheVersion, relativePath) {
            try {
                const key = cacheVersion + '::' + String(relativePath).replace(/\\/g, '/');
                const db = await getDb();
                const value = await new Promise((resolve, reject) => {
                    const tx = db.transaction(STORE_NAME, 'readonly');
                    const req = tx.objectStore(STORE_NAME).get(key);
                    req.onsuccess = () => resolve(req.result);
                    req.onerror = () => reject(req.error);
                });
                if (value == null)
                    return null;
                return value instanceof ArrayBuffer ? new Uint8Array(value) : value;
            }
            catch (e) {
                console.warn('[openra-asset-cache] tryGet', e);
                return null;
            }
        },
        tryPut: async function (cacheVersion, relativePath, bytes) {
            try {
                const key = cacheVersion + '::' + String(relativePath).replace(/\\/g, '/');
                const db = await getDb();
                const payload = bytes.slice ? bytes.slice() : bytes;
                await new Promise((resolve, reject) => {
                    const tx = db.transaction(STORE_NAME, 'readwrite');
                    tx.objectStore(STORE_NAME).put(payload, key);
                    tx.oncomplete = () => resolve();
                    tx.onerror = () => reject(tx.error);
                    tx.onabort = () => reject(tx.error || new Error('aborted'));
                });
            }
            catch (e) {
                console.warn('[openra-asset-cache] tryPut', e);
            }
        },
    };
})();
