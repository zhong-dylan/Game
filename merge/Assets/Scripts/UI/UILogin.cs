using UnityEngine;

public class UILogin : PageBase
{
    public override string PrefabPath => "Prefabs/UILogin";

    protected override void OnInitUI()
    {
        base.OnInitUI();

        SetClickListener("btn_login", OnLoginClicked);
    }

    void OnLoginClicked(GameObject go)
    {
        Log.Debug("Login button clicked."); 
    }
}
