using UnityEngine;

[CreateAssetMenu(fileName = "LaunchConfig", menuName = "Game/Launch Config")]
public class LaunchConfig : ScriptableObject
{
    [SerializeField] private string serverUrl;

    public string ServerUrl => serverUrl;
}
