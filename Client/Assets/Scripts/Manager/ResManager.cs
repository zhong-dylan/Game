using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace Game
{
    public class ResManager : MonoSingleton<ResManager>
    {
        public async Task<SceneInstance> LoadSceneASync(string scenePath, LoadSceneMode mode, bool active)
        {
            AsyncOperationHandle<SceneInstance> handle = Addressables.LoadSceneAsync(scenePath, mode, active);
            await handle.Task;
            return handle.Result;
        }

        public async Task UnloadSceneAync(SceneInstance instance)
        {
            var handle = Addressables.UnloadSceneAsync(instance);
            await handle.Task;
        }

        public async Task<GameObject> InstantiateAsync(object key, Vector3 position, Quaternion rotation, Transform parent = null, bool trackHandle = true)
        {
            AsyncOperationHandle<GameObject> handle = Addressables.InstantiateAsync(key, position, rotation, parent, trackHandle);
            await handle.Task;
            return handle.Result;
        }

        public async Task<T> LoadAssetAsync<T>(string filePath)
        {
            AsyncOperationHandle<T> handle = Addressables.LoadAssetAsync<T>(filePath);
            await handle.Task;
            return handle.Result;
        }
    }
}
