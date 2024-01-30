using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Game
{
    public class Loader : MonoBehaviour
    {
        private Dictionary<string, Object> m_DicLoadedRes = new Dictionary<string, Object>();

        private void OnDestroy()
        {
            foreach(var item in m_DicLoadedRes)
            {
                Addressables.Release(item.Value);
            }
        }
    }
}
