using TMPro;
using UnityEngine;

public class UILogin : PageBase
{
    private const string FontAddress = "Fonts/fzcy";

    public override string PrefabPath => "Prefabs/UILogin";

    protected override void OnInitUI()
    {
        base.OnInitUI();

        SetClickListener("btn_login", OnLoginClicked);
    }

    void OnLoginClicked(GameObject go)
    {
        AssetsLoader loader = AppEntry.I == null ? null : AppEntry.I.GetComponent<AssetsLoader>();
        if (loader == null)
        {
            Log.Error("UILogin.OnLoginClicked failed: AssetsLoader not found.");
            return;
        }

        TMP_Text label = go == null ? null : go.GetComponentInChildren<TMP_Text>(true);
        if (label == null)
        {
            Log.Error("UILogin.OnLoginClicked failed: TMP_Text not found under btn_login.");
            return;
        }

        loader.LoadAssetAsync<Font>(FontAddress, font =>
        {
            if (font == null)
            {
                Log.Error($"UILogin.OnLoginClicked failed: can not load font. address={FontAddress}");
                return;
            }

            label.font = TMP_FontAsset.CreateFontAsset(font);
            Log.Debug($"UILogin.OnLoginClicked loaded font: {FontAddress}");
        });
    }
}
