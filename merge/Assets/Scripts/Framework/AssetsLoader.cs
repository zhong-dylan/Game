using System.Collections.Generic;
using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public class AssetsLoader : MonoBehaviour
{
    private static readonly object AssetsMgrAccessToken = AssetsMgr.GetLoaderAccessToken();
    private readonly Dictionary<string, int> retainedAddresses = new Dictionary<string, int>();

    public void LoadAssetAsync<T>(string address, Action<T> onLoaded) where T : Object
    {
        AssetsMgr.I.LoadAssetAsync<T>(AssetsMgrAccessToken, address, asset =>
        {
            if (asset == null)
            {
                onLoaded?.Invoke(null);
                return;
            }

            RetainAddress(address);
            onLoaded?.Invoke(asset);
        });
    }

    public void CreateGameObjectAsync(string address, Action<GameObject> onCreated, Transform parent = null, bool worldPositionStays = false)
    {
        AssetsMgr.I.LoadAssetAsync<GameObject>(AssetsMgrAccessToken, address, prefab =>
        {
            if (prefab == null)
            {
                onCreated?.Invoke(null);
                return;
            }

            GameObject instance = Instantiate(prefab, parent, worldPositionStays);
            AssetsLoader loader = instance.GetComponent<AssetsLoader>();
            if (loader == null)
            {
                loader = instance.AddComponent<AssetsLoader>();
            }

            loader.RetainAddress(address);
            onCreated?.Invoke(instance);
        });
    }

    public void LoadSceneAsync(string address, LoadSceneMode loadMode = LoadSceneMode.Single, Action<bool> onLoaded = null)
    {
        AssetsMgr.I.LoadSceneAsync(AssetsMgrAccessToken, address, loadMode, onLoaded);
    }

    public void UnloadSceneAsync(string address, Action<bool> onUnloaded = null)
    {
        AssetsMgr.I.UnloadSceneAsync(AssetsMgrAccessToken, address, onUnloaded);
    }

    public void Release(string address)
    {
        int count;
        if (!retainedAddresses.TryGetValue(address, out count))
        {
            return;
        }

        count--;
        if (count > 0)
        {
            retainedAddresses[address] = count;
            AssetsMgr.I.ReleaseAsset(AssetsMgrAccessToken, address);
            return;
        }

        retainedAddresses.Remove(address);
        AssetsMgr.I.ReleaseAsset(AssetsMgrAccessToken, address);
    }

    private void OnDestroy()
    {
        AssetsMgr assetsMgr = AssetsMgr.I;
        if (assetsMgr == null)
        {
            retainedAddresses.Clear();
            return;
        }

        foreach (KeyValuePair<string, int> pair in retainedAddresses)
        {
            for (int i = 0; i < pair.Value; i++)
            {
                assetsMgr.ReleaseAsset(AssetsMgrAccessToken, pair.Key);
            }
        }

        retainedAddresses.Clear();
    }

    private void RetainAddress(string address)
    {
        int count;
        retainedAddresses.TryGetValue(address, out count);
        retainedAddresses[address] = count + 1;
    }
}
