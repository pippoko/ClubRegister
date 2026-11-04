using UnityEngine;

/// <summary>
/// Scene-level controls for the runtime-built register UI.
/// Select the scene Canvas and adjust these values in the Inspector.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("Club Register/Offline Register Layout")]
public sealed class OfflineRegisterLayout : MonoBehaviour
{
    [Header("画面サイズ")]
    [Tooltip("UIを配置する基準サイズ。スマートフォン向けの縦長比率です。")]
    public Vector2 referenceResolution = new Vector2(430, 932);

    [Range(0f, 1f)]
    [Tooltip("画面サイズに合わせる比率。0は幅、1は高さを優先します。")]
    public float matchWidthOrHeight = 0.45f;

    [Header("全体の余白")]
    [Min(0)] public int horizontalPadding = 12;
    [Min(0)] public int verticalPadding = 10;
    [Min(0)] public int sectionSpacing = 8;
    [Min(0)] public int pagePadding = 8;

    [Header("画面領域の高さ")]
    [Min(0f)] public float headerHeight = 66f;
    [Min(0f)] public float footerHeight = 144f;
    [Min(0f)] public float pageMinimumHeight = 250f;

    [Header("商品一覧")]
    [Range(1, 6)] public int compactColumnCount = 3;
    [Range(1, 6)] public int wideColumnCount = 4;
    [Min(1f)] public float wideLayoutBreakpoint = 720f;
    [Min(0f)] public float productTileSpacing = 8f;
    [Min(0.5f)] public float productTileHeightRatio = 1.28f;
}
