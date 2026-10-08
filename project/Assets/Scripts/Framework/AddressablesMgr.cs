using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// 仅在 Unity 主线程调用。同一地址和类型共享加载，每次加载对应一次卸载。
/// </summary>
public class AddressablesMgr : MonoSingle<AddressablesMgr>
{
    private sealed class AssetEntry
    {
        public AsyncOperationHandle Handle;
        public int ReferenceCount;
        public bool Released;
        public bool HandleReleased;
        public readonly TaskCompletionSource<UnityEngine.Object> Completion =
            new TaskCompletionSource<UnityEngine.Object>();
    }

    private readonly Dictionary<(string, Type), AssetEntry> assets =
        new Dictionary<(string, Type), AssetEntry>();
    private TaskCompletionSource<bool> initialization;
    private bool destroyed;

    public bool IsInitialized { get; private set; }

    protected override void Awake()
    {
        base.Awake();
        if (Instance == this && transform.parent == null)
            DontDestroyOnLoad(gameObject);
    }

    /// <summary>并发调用共享初始化；失败后可重试。</summary>
    public Task InitializeAsync()
    {
        if (destroyed)
            return Task.FromException(new ObjectDisposedException(nameof(AddressablesMgr)));
        if (IsInitialized)
            return Task.CompletedTask;
        if (initialization != null)
            return initialization.Task;

        var completion = new TaskCompletionSource<bool>();
        initialization = completion;
        try
        {
            // 在完成回调结束后由 Addressables 自动释放初始化句柄。
            Addressables.InitializeAsync().Completed += handle =>
            {
                if (destroyed)
                {
                    completion.TrySetCanceled();
                    return;
                }

                IsInitialized = handle.Status == AsyncOperationStatus.Succeeded;
                if (IsInitialized)
                    completion.TrySetResult(true);
                else
                {
                    initialization = null;
                    completion.TrySetException(handle.OperationException ??
                        new InvalidOperationException("Addressables 初始化失败。"));
                }
            };
        }
        catch (Exception exception)
        {
            initialization = null;
            completion.TrySetException(exception);
        }
        return completion.Task;
    }

    /// <summary>
    /// 加载并持有资源。每次成功调用后需配对 UnloadAsset&lt;T&gt;(address)。
    /// Prefab 通过 Object.Instantiate 创建的对象必须先销毁，再卸载资源。
    /// </summary>
    public async Task<T> LoadAssetAsync<T>(string address) where T : UnityEngine.Object
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new ArgumentException("资源地址不能为空。", nameof(address));

        await InitializeAsync();
        if (destroyed)
            throw new ObjectDisposedException(nameof(AddressablesMgr));

        var key = (address, typeof(T));
        AssetEntry entry;
        if (!assets.TryGetValue(key, out entry))
        {
            var handle = Addressables.LoadAssetAsync<T>(address);
            entry = new AssetEntry { Handle = handle, ReferenceCount = 1 };
            assets.Add(key, entry);
            var loadingEntry = entry;
            handle.Completed += operation =>
            {
                if (loadingEntry.Released)
                {
                    ReleaseHandle(loadingEntry);
                    return;
                }

                if (operation.Status == AsyncOperationStatus.Succeeded)
                {
                    loadingEntry.Completion.TrySetResult(operation.Result);
                }
                else
                {
                    assets.Remove(key);
                    loadingEntry.Released = true;
                    var exception = operation.OperationException ??
                        new InvalidOperationException($"资源加载失败：{address}");
                    ReleaseHandle(loadingEntry);
                    loadingEntry.Completion.TrySetException(exception);
                }
            };
        }
        else
        {
            entry.ReferenceCount++;
        }

        return (T)await entry.Completion.Task;
    }

    /// <summary>减少一次引用，最后一个引用释放时卸载；支持取消尚未完成的加载。</summary>
    public bool UnloadAsset<T>(string address) where T : UnityEngine.Object
    {
        if (string.IsNullOrWhiteSpace(address))
            return false;
        var key = (address, typeof(T));
        if (!assets.TryGetValue(key, out var entry))
            return false;

        if (--entry.ReferenceCount == 0)
        {
            assets.Remove(key);
            ReleaseEntry(entry);
        }
        return true;
    }

    /// <summary>强制释放全部资源，取消未完成的加载；调用后所有资源引用失效。</summary>
    public void UnloadAll()
    {
        // 先清空字典，避免取消任务的后续逻辑修改正在遍历的集合。
        var entries = new List<AssetEntry>(assets.Values);
        assets.Clear();
        foreach (var entry in entries)
            ReleaseEntry(entry);
    }

    private static void ReleaseEntry(AssetEntry entry)
    {
        if (entry.Released)
            return;
        entry.Released = true;
        // 未完成句柄由完成回调释放，确保回调读取时句柄仍有效。
        if (entry.Handle.IsValid() && entry.Handle.IsDone)
            ReleaseHandle(entry);
        entry.Completion.TrySetCanceled();
    }

    private static void ReleaseHandle(AssetEntry entry)
    {
        if (entry.HandleReleased)
            return;
        entry.HandleReleased = true;
        if (entry.Handle.IsValid())
            Addressables.Release(entry.Handle);
    }

    protected override void OnDestroy()
    {
        destroyed = true;
        initialization?.TrySetCanceled();
        UnloadAll();
        IsInitialized = false;
        base.OnDestroy();
    }
}
