using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Game
{
    public class GameManager : MonoSingleton<GameManager>
    {
        public Loader ResLoad;

        public override void Init()
        {
            base.Init();

            ResLoad = gameObject.AddComponent<Loader>();
        }
    }
}
