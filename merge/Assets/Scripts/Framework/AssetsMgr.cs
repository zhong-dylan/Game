using System.Collections.Generic;
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public class AssetsMgr : MonoSingle<AssetsMgr>
{
    private sealed class LoaderAccessToken
    {
    }

    private class AssetRecord
    {
        public AsyncOperationHandle Handle;
        public int RefCount;
    }

    private class SceneRecord
    {
        public AsyncOperationHandle<SceneInstance> Handle;
        public int RefCount;
    }

    private static readonly LoaderAccessToken AccessTokenInstance = new LoaderAccessToken();
    private readonly Dictionary<string, AssetRecord> assetRecords = new Dictionary<string, AssetRecord>();
    private readonly Dictionary<string, SceneRecord> sceneRecords = new Dictionary<string, SceneRecord>();

    internal static object GetLoaderAccessToken()
    {
        return AccessTokenInstance;
    }

    internal void GetDownloadSizeAsync(object accessToken, string address, Action<long> onCompleted)
    {
        if (!ReferenceEquals(accessToken, AccessTokenInstance))
        {
            Log.Error("AssetsMgr.GetDownloadSizeAsync denied: caller is not AssetsLoader.");
            onCompleted?.Invoke(0L);
            return;
        }

        if (string.IsNullOrEmpty(address))
        {
            Log.Error("AssetsMgr.GetDownloadSizeAsync failed: address is null or empty.");
            onCompleted?.Invoke(0L);
            return;
        }

        StartCoroutine(GetDownloadSizeRoutine(address, onCompleted));
    }

    internal void DownloadDependenciesAsync(object accessToken, string address, Action<bool, long> onCompleted)
    {
        if (!ReferenceEquals(accessToken, AccessTokenInstance))
        {
            Log.Error("AssetsMgr.DownloadDependenciesAsync denied: caller is not AssetsLoader.");
            onCompleted?.Invoke(false, 0L);
            return;
        }

        if (string.IsNullOrEmpty(address))
        {
            Log.Error("AssetsMgr.DownloadDependenciesAsync failed: address is null or empty.");
            onCompleted?.Invoke(false, 0L);
            return;
        }

        StartCoroutine(DownloadDependenciesRoutine(address, onCompleted));
    }

    internal void LoadAssetAsync<T>(object accessToken, string address, Action<T> onLoaded) where T : Object
    {
        if (!ReferenceEquals(accessToken, AccessTokenInstance))
        {
            Log.Error("AssetsMgr.LoadAssetAsync denied: caller is not AssetsLoader.");
            onLoaded?.Invoke(null);
            return;
        }

        if (string.IsNullOrEmpty(address))
        {
            Log.Error("AssetsMgr.LoadAssetAsync failed: address is null or empty.");
            onLoaded?.Invoke(null);
            return;
        }

        AssetRecord record;
        if (assetRecords.TryGetValue(address, out record))
        {
            record.RefCount++;
            StartCoroutine(WaitForHandle(address, record, onLoaded));
            return;
        }

        StartCoroutine(LoadAssetWithDependencies(address, onLoaded));
    }

    internal void ReleaseAsset(object accessToken, string address)
    {
        if (!ReferenceEquals(accessToken, AccessTokenInstance))
        {
            Log.Error("AssetsMgr.ReleaseAsset denied: caller is not AssetsLoader.");
            return;
        }

        ReleaseAssetInternal(address);
    }

    internal void LoadSceneAsync(object accessToken, string address, LoadSceneMode loadMode, Action<bool> onLoaded)
    {
        if (!ReferenceEquals(accessToken, AccessTokenInstance))
        {
            Log.Error("AssetsMgr.LoadSceneAsync denied: caller is not AssetsLoader.");
            onLoaded?.Invoke(false);
            return;
        }

        if (string.IsNullOrEmpty(address))
        {
            Log.Error("AssetsMgr.LoadSceneAsync failed: address is null or empty.");
            onLoaded?.Invoke(false);
            return;
        }

        if (loadMode == LoadSceneMode.Single)
        {
            StartCoroutine(LoadSingleSceneWithDependencies(address, onLoaded));
            return;
        }

        SceneRecord record;
        if (sceneRecords.TryGetValue(address, out record))
        {
            record.RefCount++;
            StartCoroutine(WaitForSceneRecord(address, record, onLoaded));
            return;
        }

        StartCoroutine(LoadAdditiveSceneWithDependencies(address, loadMode, onLoaded));
    }

    internal void UnloadSceneAsync(object accessToken, string address, Action<bool> onUnloaded)
    {
        if (!ReferenceEquals(accessToken, AccessTokenInstance))
        {
            Log.Error("AssetsMgr.UnloadSceneAsync denied: caller is not AssetsLoader.");
            onUnloaded?.Invoke(false);
            return;
        }

        if (string.IsNullOrEmpty(address))
        {
            Log.Error("AssetsMgr.UnloadSceneAsync failed: address is null or empty.");
            onUnloaded?.Invoke(false);
            return;
        }

        SceneRecord record;
        if (!sceneRecords.TryGetValue(address, out record))
        {
            onUnloaded?.Invoke(false);
            return;
        }

        record.RefCount--;
        if (record.RefCount > 0)
        {
            onUnloaded?.Invoke(true);
            return;
        }

        sceneRecords.Remove(address);
        AsyncOperationHandle<SceneInstance> unloadHandle = Addressables.UnloadSceneAsync(record.Handle, true);
        StartCoroutine(WaitForSceneUnload(address, unloadHandle, onUnloaded));
    }

    private void ReleaseAssetInternal(string address)
    {
        if (string.IsNullOrEmpty(address))
        {
            return;
        }

        AssetRecord record;
        if (!assetRecords.TryGetValue(address, out record))
        {
            return;
        }

        record.RefCount--;
        if (record.RefCount > 0)
        {
            return;
        }

        if (record.Handle.IsValid())
        {
            Addressables.Release(record.Handle);
        }

        assetRecords.Remove(address);
    }

    protected override void OnDestroy()
    {
        foreach (KeyValuePair<string, AssetRecord> pair in assetRecords)
        {
            if (pair.Value.Handle.IsValid())
            {
                Addressables.Release(pair.Value.Handle);
            }
        }

        foreach (KeyValuePair<string, SceneRecord> pair in sceneRecords)
        {
            if (pair.Value.Handle.IsValid())
            {
                Addressables.UnloadSceneAsync(pair.Value.Handle, true);
            }
        }

        assetRecords.Clear();
        sceneRecords.Clear();
        base.OnDestroy();
    }

    private IEnumerator GetDownloadSizeRoutine(string address, Action<long> onCompleted)
    {
        AsyncOperationHandle<long> handle = Addressables.GetDownloadSizeAsync(address);
        yield return handle;

        if (handle.Status != AsyncOperationStatus.Succeeded)
        {
            Log.Error($"AssetsMgr failed to get download size: {address}");
            onCompleted?.Invoke(0L);
        }
        else
        {
            onCompleted?.Invoke(handle.Result);
        }

        if (handle.IsValid())
        {
            Addressables.Release(handle);
        }
    }

    private IEnumerator DownloadDependenciesRoutine(string address, Action<bool, long> onCompleted)
    {
        AsyncOperationHandle<long> sizeHandle = Addressables.GetDownloadSizeAsync(address);
        yield return sizeHandle;

        if (sizeHandle.Status != AsyncOperationStatus.Succeeded)
        {
            Log.Error($"AssetsMgr failed to get dependency download size: {address}");
            onCompleted?.Invoke(false, 0L);
            if (sizeHandle.IsValid())
            {
                Addressables.Release(sizeHandle);
            }
            yield break;
        }

        long downloadSize = sizeHandle.Result;
        if (sizeHandle.IsValid())
        {
            Addressables.Release(sizeHandle);
        }

        if (downloadSize <= 0L)
        {
            onCompleted?.Invoke(true, 0L);
            yield break;
        }

        Log.Debug($"AssetsMgr downloading dependencies: {address}, size={downloadSize} bytes");
        AsyncOperationHandle downloadHandle = Addressables.DownloadDependenciesAsync(address, false);
        yield return downloadHandle;

        if (downloadHandle.Status != AsyncOperationStatus.Succeeded)
        {
            Log.Error($"AssetsMgr failed to download dependencies: {address}");
            onCompleted?.Invoke(false, downloadSize);
            if (downloadHandle.IsValid())
            {
                Addressables.Release(downloadHandle);
            }
            yield break;
        }

        if (downloadHandle.IsValid())
        {
            Addressables.Release(downloadHandle);
        }

        onCompleted?.Invoke(true, downloadSize);
    }

    private IEnumerator LoadAssetWithDependencies<T>(string address, Action<T> onLoaded) where T : Object
    {
        AsyncOperationHandle<long> sizeHandle = Addressables.GetDownloadSizeAsync(address);
        yield return sizeHandle;

        if (sizeHandle.Status != AsyncOperationStatus.Succeeded)
        {
            Log.Error($"AssetsMgr failed to get download size before loading asset: {address}");
            onLoaded?.Invoke(null);
            if (sizeHandle.IsValid())
            {
                Addressables.Release(sizeHandle);
            }
            yield break;
        }

        long downloadSize = sizeHandle.Result;
        if (sizeHandle.IsValid())
        {
            Addressables.Release(sizeHandle);
        }

        if (downloadSize > 0L)
        {
            AsyncOperationHandle downloadHandle = Addressables.DownloadDependenciesAsync(address, false);
            yield return downloadHandle;
            if (downloadHandle.Status != AsyncOperationStatus.Succeeded)
            {
                Log.Error($"AssetsMgr failed to download dependencies before loading asset: {address}");
                onLoaded?.Invoke(null);
                if (downloadHandle.IsValid())
                {
                    Addressables.Release(downloadHandle);
                }
                yield break;
            }

            if (downloadHandle.IsValid())
            {
                Addressables.Release(downloadHandle);
            }
        }

        AssetRecord record = new AssetRecord
        {
            Handle = Addressables.LoadAssetAsync<T>(address),
            RefCount = 1
        };
        assetRecords[address] = record;
        StartCoroutine(WaitForHandle(address, record, onLoaded));
    }

    private IEnumerator LoadSingleSceneWithDependencies(string address, Action<bool> onLoaded)
    {
        AsyncOperationHandle<long> sizeHandle = Addressables.GetDownloadSizeAsync(address);
        yield return sizeHandle;

        if (sizeHandle.Status != AsyncOperationStatus.Succeeded)
        {
            Log.Error($"AssetsMgr failed to get download size before loading scene: {address}");
            onLoaded?.Invoke(false);
            if (sizeHandle.IsValid())
            {
                Addressables.Release(sizeHandle);
            }
            yield break;
        }

        long downloadSize = sizeHandle.Result;
        if (sizeHandle.IsValid())
        {
            Addressables.Release(sizeHandle);
        }

        if (downloadSize > 0L)
        {
            AsyncOperationHandle downloadHandle = Addressables.DownloadDependenciesAsync(address, false);
            yield return downloadHandle;
            if (downloadHandle.Status != AsyncOperationStatus.Succeeded)
            {
                Log.Error($"AssetsMgr failed to download dependencies before loading scene: {address}");
                onLoaded?.Invoke(false);
                if (downloadHandle.IsValid())
                {
                    Addressables.Release(downloadHandle);
                }
                yield break;
            }

            if (downloadHandle.IsValid())
            {
                Addressables.Release(downloadHandle);
            }
        }

        AsyncOperationHandle<SceneInstance> handle = Addressables.LoadSceneAsync(address, LoadSceneMode.Single);
        StartCoroutine(WaitForSceneHandle(address, handle, onLoaded));
    }

    private IEnumerator LoadAdditiveSceneWithDependencies(string address, LoadSceneMode loadMode, Action<bool> onLoaded)
    {
        AsyncOperationHandle<long> sizeHandle = Addressables.GetDownloadSizeAsync(address);
        yield return sizeHandle;

        if (sizeHandle.Status != AsyncOperationStatus.Succeeded)
        {
            Log.Error($"AssetsMgr failed to get download size before loading scene: {address}");
            onLoaded?.Invoke(false);
            if (sizeHandle.IsValid())
            {
                Addressables.Release(sizeHandle);
            }
            yield break;
        }

        long downloadSize = sizeHandle.Result;
        if (sizeHandle.IsValid())
        {
            Addressables.Release(sizeHandle);
        }

        if (downloadSize > 0L)
        {
            AsyncOperationHandle downloadHandle = Addressables.DownloadDependenciesAsync(address, false);
            yield return downloadHandle;
            if (downloadHandle.Status != AsyncOperationStatus.Succeeded)
            {
                Log.Error($"AssetsMgr failed to download dependencies before loading scene: {address}");
                onLoaded?.Invoke(false);
                if (downloadHandle.IsValid())
                {
                    Addressables.Release(downloadHandle);
                }
                yield break;
            }

            if (downloadHandle.IsValid())
            {
                Addressables.Release(downloadHandle);
            }
        }

        SceneRecord record = new SceneRecord
        {
            Handle = Addressables.LoadSceneAsync(address, loadMode),
            RefCount = 1
        };
        sceneRecords[address] = record;
        StartCoroutine(WaitForSceneRecord(address, record, onLoaded));
    }

    private IEnumerator WaitForHandle<T>(string address, AssetRecord record, Action<T> onLoaded) where T : Object
    {
        yield return record.Handle;

        if (record.Handle.Status != AsyncOperationStatus.Succeeded)
        {
            Log.Error($"AssetsMgr failed to load address: {address}");
            ReleaseAssetInternal(address);
            onLoaded?.Invoke(null);
            yield break;
        }

        T asset = record.Handle.Result as T;
        if (asset == null)
        {
            Log.Error($"AssetsMgr loaded address with unexpected type: {address}");
            ReleaseAssetInternal(address);
        }

        onLoaded?.Invoke(asset);
    }

    private IEnumerator WaitForSceneHandle(string address, AsyncOperationHandle<SceneInstance> handle, Action<bool> onLoaded)
    {
        yield return handle;

        if (handle.Status != AsyncOperationStatus.Succeeded)
        {
            Log.Error($"AssetsMgr failed to load scene address: {address}");
            onLoaded?.Invoke(false);
            yield break;
        }

        onLoaded?.Invoke(true);
    }

    private IEnumerator WaitForSceneRecord(string address, SceneRecord record, Action<bool> onLoaded)
    {
        yield return record.Handle;

        if (record.Handle.Status != AsyncOperationStatus.Succeeded)
        {
            Log.Error($"AssetsMgr failed to load scene address: {address}");
            sceneRecords.Remove(address);
            onLoaded?.Invoke(false);
            yield break;
        }

        onLoaded?.Invoke(true);
    }

    private IEnumerator WaitForSceneUnload(string address, AsyncOperationHandle<SceneInstance> handle, Action<bool> onUnloaded)
    {
        yield return handle;

        if (handle.Status != AsyncOperationStatus.Succeeded)
        {
            Log.Error($"AssetsMgr failed to unload scene address: {address}");
            onUnloaded?.Invoke(false);
            yield break;
        }

        onUnloaded?.Invoke(true);
    }
}
