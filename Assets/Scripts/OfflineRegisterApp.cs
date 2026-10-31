using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Runtime-created portrait register UI. Keeping the first screen code-driven lets
/// the existing SampleScene remain intact while the offline workflow is built out.
/// </summary>
public class OfflineRegisterApp : MonoBehaviour
{
    private enum Page
    {
        Home,
        Register,
        Cart,
        Payment,
        Receipt,
        Products,
        ProductForm,
        History,
        EndEvent
    }

    private static OfflineRegisterApp instance;

    private static readonly Color Ink = new Color(0.12f, 0.16f, 0.19f);
    private static readonly Color Muted = new Color(0.35f, 0.40f, 0.43f);
    private static readonly Color Paper = new Color(0.96f, 0.96f, 0.94f);
    private static readonly Color Card = Color.white;
    private static readonly Color Primary = new Color(0.12f, 0.38f, 0.39f);
    private static readonly Color PrimaryLight = new Color(0.80f, 0.91f, 0.89f);
    private static readonly Color Accent = new Color(0.84f, 0.40f, 0.37f);
    private static readonly Color AccentLight = new Color(0.98f, 0.85f, 0.82f);
    private static readonly Color WarningLight = new Color(1f, 0.94f, 0.73f);

    private ProductCatalog catalog;
    private OfflineRegisterStore store;
    private Canvas canvas;
    private RectTransform safeRoot;
    private RectTransform header;
    private RectTransform body;
    private RectTransform footer;
    private Font uiFont;

    private Page currentPage;
    private readonly List<CartItem> cart = new List<CartItem>();
    private string editingProductID;
    private InputField eventNameInput;
    private InputField productNameInput;
    private InputField productPriceInput;
    private InputField productStockInput;
    private InputField productImagePathInput;
    private long receivedAmount;
    private long lastSaleTotal;
    private long lastSaleReceived;
    private long lastSaleChange;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if(FindFirstObjectByType<OfflineRegisterApp>() != null)
        {
            return;
        }

        var appObject = new GameObject("Offline Register App");
        appObject.AddComponent<OfflineRegisterApp>();
    }

    private void Awake()
    {
        if(instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        Screen.orientation = ScreenOrientation.Portrait;
        Screen.sleepTimeout = SleepTimeout.SystemSetting;

        catalog = FindFirstObjectByType<ProductCatalog>();
        if(catalog == null)
        {
            var catalogObject = new GameObject("Product Catalog");
            catalog = catalogObject.AddComponent<ProductCatalog>();
        }

        store = gameObject.AddComponent<OfflineRegisterStore>();
        store.RetryFinalization(catalog);
        PrepareEventSystem();
        CreateCanvas();
        ShowPage(store.ActiveEvent == null
            ? Page.Home
            : (store.IsFinalizing ? Page.EndEvent : Page.Register));
    }

    private void LateUpdate()
    {
        if(safeRoot == null || Screen.width <= 0 || Screen.height <= 0)
        {
            return;
        }

        Rect area = Screen.safeArea;
        safeRoot.anchorMin = new Vector2(area.xMin / Screen.width, area.yMin / Screen.height);
        safeRoot.anchorMax = new Vector2(area.xMax / Screen.width, area.yMax / Screen.height);
    }

    private void OnDestroy()
    {
        if(instance == this)
        {
            instance = null;
        }
    }

    private void PrepareEventSystem()
    {
        if(EventSystem.current != null)
        {
            return;
        }

        var eventSystemObject = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        DontDestroyOnLoad(eventSystemObject);
    }

    private void CreateCanvas()
    {
        Canvas[] sceneCanvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
        foreach(Canvas sceneCanvas in sceneCanvases)
        {
            if(sceneCanvas != null && sceneCanvas.gameObject != gameObject)
            {
                sceneCanvas.gameObject.SetActive(false);
            }
        }

        var canvasObject = new GameObject("Offline Register Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(430, 932);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.45f;

        uiFont = CreateJapaneseFont();
        safeRoot = CreateRect("Safe Area", canvasObject.transform);
        safeRoot.anchorMin = Vector2.zero;
        safeRoot.anchorMax = Vector2.one;
        safeRoot.offsetMin = Vector2.zero;
        safeRoot.offsetMax = Vector2.zero;
        safeRoot.gameObject.AddComponent<Image>().color = Paper;

        VerticalLayoutGroup rootLayout = safeRoot.gameObject.AddComponent<VerticalLayoutGroup>();
        rootLayout.padding = new RectOffset(12, 12, 10, 10);
        rootLayout.spacing = 8;
        rootLayout.childControlWidth = true;
        rootLayout.childControlHeight = true;
        rootLayout.childForceExpandWidth = true;
        rootLayout.childForceExpandHeight = false;

        header = CreatePanel("Header", safeRoot, PrimaryLight, 66);
        body = CreatePanel("Page", safeRoot, Paper, -1);
        footer = CreatePanel("Actions", safeRoot, Paper, 144);
        body.GetComponent<LayoutElement>().flexibleHeight = 1;
        body.GetComponent<LayoutElement>().minHeight = 250;
    }

    private Font CreateJapaneseFont()
    {
#if UNITY_ANDROID
        string[] fonts = { "NotoSansCJKjp-Regular", "Noto Sans JP", "sans-serif", "Arial" };
#elif UNITY_IOS
        string[] fonts = { "Hiragino Sans", "Arial" };
#else
        string[] fonts = { "Yu Gothic UI", "Meiryo UI", "Meiryo", "Arial" };
#endif
        return Font.CreateDynamicFontFromOSFont(fonts, 28);
    }

    private void ShowPage(Page page)
    {
        currentPage = page;
        ClearChildren(header);
        ClearChildren(body);
        ClearChildren(footer);
        BuildHeader();

        switch(currentPage)
        {
            case Page.Home:
                BuildHome();
                BuildHomeFooter();
                break;
            case Page.Register:
                BuildRegister();
                BuildRegisterFooter();
                break;
            case Page.Cart:
                BuildCart();
                BuildCartFooter();
                break;
            case Page.Payment:
                BuildPayment();
                BuildPaymentFooter();
                break;
            case Page.Receipt:
                BuildReceipt();
                BuildReceiptFooter();
                break;
            case Page.Products:
                BuildProducts();
                BuildProductsFooter();
                break;
            case Page.ProductForm:
                BuildProductForm();
                BuildProductFormFooter();
                break;
            case Page.History:
                BuildHistory();
                BuildHistoryFooter();
                break;
            case Page.EndEvent:
                BuildEndEvent();
                BuildEndEventFooter();
                break;
        }
    }

    private void BuildHeader()
    {
        HorizontalLayoutGroup row = header.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.padding = new RectOffset(14, 14, 6, 6);
        row.spacing = 8;
        row.childAlignment = TextAnchor.MiddleCenter;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = true;

        string title = store.ActiveEvent == null ? "Club Register" : store.ActiveEvent.eventName;
        Text titleText = CreateText(header, title, 22, Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
        titleText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;

        string state = store.ActiveEvent == null ? "オフライン" : "● オフライン";
        Text stateText = CreateText(header, state, 14, Primary, TextAnchor.MiddleRight, FontStyle.Bold);
        stateText.gameObject.AddComponent<LayoutElement>().preferredWidth = 94;
    }

    private void BuildHome()
    {
        ConfigureVerticalPage(18);
        CreateLabel(body, "イベント会計", 28, Ink, FontStyle.Bold, 46);
        CreateLabel(body, "電波がなくても会計できます。イベント中の在庫は仮の数として記録し、終了時に確定します。", 16, Muted, FontStyle.Normal, 68);
        CreateLabel(body, "イベント名", 16, Ink, FontStyle.Bold, 28);
        eventNameInput = CreateInput(body, "例：北ティア24", false);
        eventNameInput.gameObject.AddComponent<LayoutElement>().preferredHeight = 56;

        if(!string.IsNullOrEmpty(store.LoadError))
        {
            CreateLabel(body, store.LoadError, 15, Accent, FontStyle.Bold, 70);
        }

        int productCount = catalog.products == null ? 0 : catalog.products.Count;
        CreateLabel(body, $"登録商品：{productCount}点", 18, Ink, FontStyle.Bold, 34);
        if(productCount == 0)
        {
            CreateLabel(body, "会計を始める前に、商品名・価格・在庫数を登録してください。", 16, Muted, FontStyle.Normal, 58);
        }
        else
        {
            CreateLabel(body, "商品を確認してイベント会計を開始できます。", 16, Muted, FontStyle.Normal, 42);
        }
    }

    private void BuildHomeFooter()
    {
        ConfigureVertical(footer, 8, 8);
        int count = catalog.products == null ? 0 : catalog.products.Count;
        Button start = CreateButton(footer, count == 0 ? "先に商品を登録" : "イベント会計を開始", Primary, Color.white, 58,
            count == 0 ? () => ShowPage(Page.Products) : StartEvent);
        start.interactable = string.IsNullOrEmpty(store.LoadError);

        RectTransform row = CreateRow(footer, 8);
        CreateButton(row, "商品管理", Card, Ink, 48, () => ShowPage(Page.Products));
        CreateButton(row, "売上履歴", Card, Ink, 48, () => ShowPage(Page.History));
    }

    private void BuildRegister()
    {
        ConfigureVerticalPage(8);

        RegisterEventRecord active = store.ActiveEvent;
        if(active == null)
        {
            ShowPage(Page.Home);
            return;
        }

        long sales = store.GetEventSalesTotal(active);
        CreateLabel(body, $"{DateTime.Now:M/d}　{active.eventName}", 17, Ink, FontStyle.Bold, 32);
        CreateLabel(body, $"本日の売上　{sales:N0}円　／　在庫はイベント終了時に確定", 14, Primary, FontStyle.Bold, 32);

        RectTransform scroll = CreateScroll(body, "商品一覧", 1);
        RectTransform content = scroll.Find("Content") as RectTransform;
        GridLayoutGroup grid = content.gameObject.AddComponent<GridLayoutGroup>();
        float logicalWidth = Screen.safeArea.width / Mathf.Max(0.5f, canvas.scaleFactor);
        int columns = logicalWidth >= 720 ? 4 : 3;
        float spacing = 8;
        float cellWidth = Mathf.Max(92, (logicalWidth - 42 - spacing * (columns - 1)) / columns);
        grid.cellSize = new Vector2(cellWidth, cellWidth * 1.28f);
        grid.spacing = new Vector2(spacing, spacing);
        grid.padding = new RectOffset(2, 2, 3, 10);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = columns;
        grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
        grid.childAlignment = TextAnchor.UpperLeft;
        ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        if(catalog.products == null || catalog.products.Count == 0)
        {
            CreateLabel(content, "商品がありません。商品管理から登録してください。", 18, Muted, FontStyle.Normal, 80);
            return;
        }

        foreach(ProductData product in catalog.products)
        {
            if(product != null)
            {
                CreateProductTile(content, product);
            }
        }
    }

    private void CreateProductTile(Transform parent, ProductData product)
    {
        int remaining = store.GetRemainingStock(product.productID);
        Button button = CreateButton(parent, string.Empty, Card, Ink, 0, () => AddToCart(product));
        button.interactable = remaining > 0;
        Image tileBackground = button.GetComponent<Image>();
        tileBackground.color = remaining > 0 ? Card : new Color(0.88f, 0.88f, 0.87f);

        VerticalLayoutGroup layout = button.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(6, 6, 6, 5);
        layout.spacing = 2;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        Image productImage = CreateImage(button.transform, Card);
        productImage.preserveAspect = true;
        productImage.raycastTarget = false;
        LayoutElement imageLayout = productImage.gameObject.AddComponent<LayoutElement>();
        imageLayout.preferredHeight = 55;
        imageLayout.flexibleHeight = 1;

        Sprite sprite = LoadProductSprite(product.imagePath);
        if(sprite != null)
        {
            productImage.sprite = sprite;
        }
        else
        {
            productImage.color = PrimaryLight;
            Text symbol = CreateText(productImage.transform, GetProductInitial(product.productName), 34, Primary, TextAnchor.MiddleCenter, FontStyle.Bold);
            symbol.raycastTarget = false;
        }

        Text name = CreateText(button.transform, product.productName, 15, Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
        name.raycastTarget = false;
        name.horizontalOverflow = HorizontalWrapMode.Wrap;
        name.gameObject.AddComponent<LayoutElement>().preferredHeight = 32;

        int price = GetDisplayPrice(product);
        Text details = CreateText(button.transform, $"{price:N0}円\n残り {remaining}", 13, remaining > 0 ? Muted : Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
        details.raycastTarget = false;
        details.gameObject.AddComponent<LayoutElement>().preferredHeight = 30;
    }

    private void BuildRegisterFooter()
    {
        ConfigureVertical(footer, 7, 6);
        RectTransform checkoutRow = CreateRow(footer, 8);
        Text totalText = CreateText(checkoutRow, $"合計\n{GetCartTotal():N0}円", 20, Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
        totalText.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        CreateButton(checkoutRow, cart.Count == 0 ? "カートを見る" : $"会計へ　{CartItemCount()}点", Primary, Color.white, 58,
            () => ShowPage(Page.Cart)).gameObject.GetComponent<LayoutElement>().flexibleWidth = 1.2f;

        RectTransform navigation = CreateRow(footer, 6);
        CreateButton(navigation, "商品管理", Card, Ink, 46, () => ShowPage(Page.Products));
        CreateButton(navigation, "売上履歴", Card, Ink, 46, () => ShowPage(Page.History));
        CreateButton(navigation, "イベント終了", AccentLight, Ink, 46, () => ShowPage(Page.EndEvent));
    }

    private void BuildCart()
    {
        ConfigureVerticalPage(8);
        CreateLabel(body, "購入商品を確認してください", 23, Ink, FontStyle.Bold, 46);

        RectTransform list = CreateVerticalScroll(body, 1);
        if(cart.Count == 0)
        {
            CreateLabel(list, "商品が選択されていません。", 17, Muted, FontStyle.Normal, 50);
            return;
        }

        foreach(CartItem item in cart)
        {
            CreateCartRow(list, item);
        }
    }

    private void CreateCartRow(Transform parent, CartItem item)
    {
        RectTransform row = CreateRow(parent, 6, Card);
        Text title = CreateText(row, $"{item.productName}\n{item.unitPrice:N0}円 × {item.quantity}", 16, Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
        title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        CreateButton(row, "−", AccentLight, Ink, 48, () => ChangeCartQuantity(item.productID, -1)).gameObject.GetComponent<LayoutElement>().preferredWidth = 54;
        CreateButton(row, "+", PrimaryLight, Ink, 48, () => ChangeCartQuantity(item.productID, 1)).gameObject.GetComponent<LayoutElement>().preferredWidth = 54;
    }

    private void BuildCartFooter()
    {
        ConfigureVertical(footer, 8, 8);
        CreateLabel(footer, $"合計　{GetCartTotal():N0}円", 26, Ink, FontStyle.Bold, 42);
        RectTransform row = CreateRow(footer, 8);
        CreateButton(row, "商品選択へ戻る", Card, Ink, 54, () => ShowPage(Page.Register));
        Button next = CreateButton(row, "お会計へ", Primary, Color.white, 54, OpenPayment);
        next.interactable = cart.Count > 0;
    }

    private void BuildPayment()
    {
        ConfigureVerticalPage(8);
        CreateLabel(body, "受け取り金額", 21, Ink, FontStyle.Bold, 38);
        CreateLabel(body, $"お会計　{GetCartTotal():N0}円", 20, Muted, FontStyle.Bold, 36);
        CreateLabel(body, $"{receivedAmount:N0}円", 36, Primary, FontStyle.Bold, 60);

        RectTransform denominationGrid = CreateRect("Cash Buttons", body);
        denominationGrid.gameObject.AddComponent<LayoutElement>().preferredHeight = 132;
        GridLayoutGroup grid = denominationGrid.gameObject.AddComponent<GridLayoutGroup>();
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 3;
        grid.spacing = new Vector2(8, 8);
        grid.cellSize = new Vector2(Mathf.Max(90, (Screen.safeArea.width / Mathf.Max(0.5f, canvas.scaleFactor) - 52) / 3), 54);
        long[] denominations = { 100, 500, 1000, 5000, 10000 };
        foreach(long denomination in denominations)
        {
            CreateButton(denominationGrid, $"{denomination:N0}円", PrimaryLight, Ink, 54, () => AddReceived(denomination));
        }
        CreateButton(denominationGrid, "ちょうど", WarningLight, Ink, 54, SetExactAmount);

        long change = Math.Max(0, receivedAmount - GetCartTotal());
        CreateLabel(body, receivedAmount < GetCartTotal()
            ? $"あと {GetCartTotal() - receivedAmount:N0}円"
            : $"おつり　{change:N0}円", 24, receivedAmount < GetCartTotal() ? Accent : Ink, FontStyle.Bold, 52);
    }

    private void BuildPaymentFooter()
    {
        ConfigureVertical(footer, 8, 8);
        RectTransform row = CreateRow(footer, 8);
        CreateButton(row, "クリア", Card, Ink, 52, () =>
        {
            receivedAmount = 0;
            ShowPage(Page.Payment);
        });
        CreateButton(row, "戻る", Card, Ink, 52, () => ShowPage(Page.Cart));
        Button confirm = CreateButton(footer, "会計を確定", Primary, Color.white, 58, ConfirmSale);
        confirm.interactable = cart.Count > 0 && receivedAmount >= GetCartTotal();
    }

    private void BuildReceipt()
    {
        ConfigureVerticalPage(16);
        CreateLabel(body, "会計が完了しました", 26, Primary, FontStyle.Bold, 54);
        CreateLabel(body, $"合計　{lastSaleTotal:N0}円", 23, Ink, FontStyle.Bold, 45);
        CreateLabel(body, $"受け取り　{lastSaleReceived:N0}円", 19, Muted, FontStyle.Normal, 36);
        CreateLabel(body, $"おつり　{lastSaleChange:N0}円", 25, Accent, FontStyle.Bold, 54);
        CreateLabel(body, "売上は端末内に保存されました。イベント中の在庫は仮の数として管理しています。", 16, Muted, FontStyle.Normal, 70);
    }

    private void BuildReceiptFooter()
    {
        ConfigureVertical(footer, 8, 8);
        CreateButton(footer, "次のお会計へ", Primary, Color.white, 60, () => ShowPage(Page.Register));
    }

    private void BuildProducts()
    {
        ConfigureVerticalPage(8);
        CreateLabel(body, "商品管理", 26, Ink, FontStyle.Bold, 46);
        if(store.ActiveEvent != null)
        {
            CreateLabel(body, "イベント中は商品情報を変更できません。在庫はイベント終了時に確定します。", 15, Accent, FontStyle.Bold, 56);
        }
        else
        {
            CreateLabel(body, "商品を追加・編集できます。画像は Resources 内に置いた Sprite を指定できます。", 15, Muted, FontStyle.Normal, 54);
        }

        RectTransform list = CreateVerticalScroll(body, 1);
        if(catalog.products == null || catalog.products.Count == 0)
        {
            CreateLabel(list, "商品がまだ登録されていません。", 17, Muted, FontStyle.Normal, 52);
            return;
        }

        foreach(ProductData product in catalog.products)
        {
            if(product == null)
            {
                continue;
            }

            RectTransform row = CreateRow(list, 8, Card);
            int stockToShow = store.ActiveEvent == null
                ? product.initialStock
                : store.GetRemainingStock(product.productID);
            Text summary = CreateText(row, $"{product.productName}\n{GetDisplayPrice(product):N0}円　・　在庫 {stockToShow}", 15, Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
            summary.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            Button edit = CreateButton(row, "編集", PrimaryLight, Ink, 54, () => OpenProductForm(product));
            edit.gameObject.GetComponent<LayoutElement>().preferredWidth = 82;
            edit.interactable = store.ActiveEvent == null;
        }
    }

    private void BuildProductsFooter()
    {
        ConfigureVertical(footer, 8, 8);
        Button add = CreateButton(footer, "＋ 商品を登録", Primary, Color.white, 58, () => OpenProductForm(null));
        add.interactable = store.ActiveEvent == null && string.IsNullOrEmpty(store.LoadError);
        CreateButton(footer, store.ActiveEvent == null ? "戻る" : "会計へ戻る", Card, Ink, 50,
            () => ShowPage(store.ActiveEvent == null ? Page.Home : Page.Register));
    }

    private void BuildProductForm()
    {
        ConfigureVerticalPage(6);
        bool editing = !string.IsNullOrEmpty(editingProductID);
        CreateLabel(body, editing ? "商品を編集" : "商品を登録", 25, Ink, FontStyle.Bold, 44);
        CreateLabel(body, "価格・在庫数はイベント会計を始める時点で記録されます。", 15, Muted, FontStyle.Normal, 42);

        CreateLabel(body, "商品名", 15, Ink, FontStyle.Bold, 24);
        productNameInput = CreateInput(body, "例：新刊イラスト集", false);
        productNameInput.gameObject.AddComponent<LayoutElement>().preferredHeight = 54;

        CreateLabel(body, "価格（円）", 15, Ink, FontStyle.Bold, 24);
        productPriceInput = CreateInput(body, "例：700", true);
        productPriceInput.gameObject.AddComponent<LayoutElement>().preferredHeight = 54;

        CreateLabel(body, "在庫数", 15, Ink, FontStyle.Bold, 24);
        productStockInput = CreateInput(body, "例：20", true);
        productStockInput.gameObject.AddComponent<LayoutElement>().preferredHeight = 54;

        CreateLabel(body, "画像パス（Resources内・任意）", 15, Ink, FontStyle.Bold, 24);
        productImagePathInput = CreateInput(body, "例：Products/new-book", false);
        productImagePathInput.gameObject.AddComponent<LayoutElement>().preferredHeight = 54;
        CreateLabel(body, "画像ファイルは Assets/Resources/ 以下に置き、拡張子を除いたパスを入力します。", 13, Muted, FontStyle.Normal, 40);
    }

    private void BuildProductFormFooter()
    {
        ConfigureVertical(footer, 8, 8);
        CreateButton(footer, "保存する", Primary, Color.white, 58, SaveProductForm);
        CreateButton(footer, "商品一覧へ戻る", Card, Ink, 48, () => ShowPage(Page.Products));
    }

    private void BuildHistory()
    {
        ConfigureVerticalPage(8);
        CreateLabel(body, "売上履歴", 26, Ink, FontStyle.Bold, 46);
        RectTransform list = CreateVerticalScroll(body, 1);

        if(store.ActiveEvent != null)
        {
            CreateEventHistoryCard(list, store.ActiveEvent, "開催中・仮集計");
        }

        IReadOnlyList<RegisterEventRecord> completed = store.CompletedEvents;
        if(completed.Count == 0 && store.ActiveEvent == null)
        {
            CreateLabel(list, "イベント履歴はまだありません。", 17, Muted, FontStyle.Normal, 48);
            return;
        }

        foreach(RegisterEventRecord record in completed)
        {
            CreateEventHistoryCard(list, record, "確定済み");
        }
    }

    private void CreateEventHistoryCard(Transform parent, RegisterEventRecord record, string status)
    {
        RectTransform card = CreateStack(parent, Card, 10, 10, 10);
        CreateLabel(card, $"{record.eventName}　{status}", 18, Ink, FontStyle.Bold, 34);
        CreateLabel(card, $"売上 {store.GetEventSalesTotal(record):N0}円　・　頒布 {store.GetEventItemCount(record)}点　・　会計 {record.sales.Count}回", 15, Muted, FontStyle.Normal, 30);
        if(!string.IsNullOrEmpty(record.startedAt))
        {
            DateTime started;
            if(DateTime.TryParse(record.startedAt, out started))
            {
                CreateLabel(card, started.ToString("yyyy/MM/dd HH:mm"), 13, Muted, FontStyle.Normal, 22);
            }
        }
    }

    private void BuildHistoryFooter()
    {
        ConfigureVertical(footer, 8, 8);
        CreateButton(footer, "戻る", Card, Ink, 56, () => ShowPage(store.ActiveEvent == null ? Page.Home : Page.Register));
    }

    private void BuildEndEvent()
    {
        ConfigureVerticalPage(14);
        RegisterEventRecord record = store.ActiveEvent;
        if(record == null)
        {
            CreateLabel(body, "開催中のイベントはありません。", 20, Muted, FontStyle.Normal, 56);
            return;
        }

        CreateLabel(body, store.IsFinalizing ? "イベント終了を再開しますか？" : "イベントを終了しますか？", 25, Ink, FontStyle.Bold, 58);
        CreateLabel(body, record.eventName, 20, Muted, FontStyle.Bold, 40);
        CreateLabel(body, $"総売り上げ\n{store.GetEventSalesTotal(record):N0}円", 28, Ink, FontStyle.Bold, 92);
        CreateLabel(body, $"頒布数　{store.GetEventItemCount(record)}点　／　会計回数　{record.sales.Count}回", 16, Muted, FontStyle.Normal, 42);
        CreateLabel(body, store.IsFinalizing
            ? "前回の終了処理が途中で止まっていました。安全に続きを実行し、在庫を確定します。"
            : "終了すると、イベント中に記録した仮の在庫変動を商品在庫へ反映し、売上履歴に保存します。", 16, Accent, FontStyle.Bold, 86);
    }

    private void BuildEndEventFooter()
    {
        ConfigureVertical(footer, 8, 8);
        if(store.IsFinalizing)
        {
            Button retry = CreateButton(footer, "在庫の確定を続ける", Accent, Color.white, 58, CompleteEvent);
            retry.interactable = string.IsNullOrEmpty(store.LoadError);
            return;
        }

        RectTransform row = CreateRow(footer, 8);
        CreateButton(row, "続ける", Card, Ink, 56, () => ShowPage(Page.Register));
        Button finish = CreateButton(row, "イベントを終了", Accent, Color.white, 56, CompleteEvent);
        finish.interactable = store.ActiveEvent != null && string.IsNullOrEmpty(store.LoadError);
    }

    private void StartEvent()
    {
        string eventName = eventNameInput == null ? string.Empty : eventNameInput.text;
        if(!store.StartEvent(eventName, catalog, out string error))
        {
            ShowNotice(error);
            return;
        }

        cart.Clear();
        ShowPage(Page.Register);
    }

    private void AddToCart(ProductData product)
    {
        if(store.ActiveEvent == null || product == null)
        {
            return;
        }

        int available = store.GetRemainingStock(product.productID);
        CartItem item = cart.Find(candidate => candidate.productID == product.productID);
        int inCart = item == null ? 0 : item.quantity;
        if(inCart >= available)
        {
            ShowNotice("この商品の在庫がありません。");
            return;
        }

        if(item == null)
        {
            RegisterProductSnapshot snapshot = store.ActiveEvent.products.Find(candidate => candidate != null && candidate.productID == product.productID);
            if(snapshot == null)
            {
                ShowNotice("イベント開始後に追加された商品です。次のイベントから販売できます。");
                return;
            }

            item = new CartItem
            {
                productID = product.productID,
                productName = snapshot.productName,
                unitPrice = snapshot.unitPrice,
                quantity = 0
            };
            cart.Add(item);
        }

        item.quantity++;
        ShowPage(Page.Register);
    }

    private void ChangeCartQuantity(string productID, int delta)
    {
        CartItem item = cart.Find(candidate => candidate.productID == productID);
        if(item == null)
        {
            return;
        }

        int next = item.quantity + delta;
        if(next <= 0)
        {
            cart.Remove(item);
        }
        else if(delta > 0 && next > store.GetRemainingStock(productID))
        {
            ShowNotice("在庫数を超える数量は選べません。");
            return;
        }
        else
        {
            item.quantity = next;
        }

        ShowPage(Page.Cart);
    }

    private void OpenPayment()
    {
        if(cart.Count == 0)
        {
            ShowNotice("商品を選択してください。");
            return;
        }

        receivedAmount = 0;
        ShowPage(Page.Payment);
    }

    private void AddReceived(long amount)
    {
        if(receivedAmount > long.MaxValue - amount)
        {
            ShowNotice("受け取り金額が大きすぎます。");
            return;
        }

        receivedAmount += amount;
        ShowPage(Page.Payment);
    }

    private void SetExactAmount()
    {
        receivedAmount = GetCartTotal();
        ShowPage(Page.Payment);
    }

    private void ConfirmSale()
    {
        if(!store.RecordSale(cart, receivedAmount, out long change, out string error))
        {
            ShowNotice(error);
            return;
        }

        lastSaleTotal = GetCartTotal();
        lastSaleReceived = receivedAmount;
        lastSaleChange = change;
        cart.Clear();
        receivedAmount = 0;
        ShowPage(Page.Receipt);
    }

    private void CompleteEvent()
    {
        if(!store.CompleteEvent(catalog, out string error))
        {
            ShowNotice(error);
            return;
        }

        cart.Clear();
        ShowPage(Page.Home);
    }

    private void OpenProductForm(ProductData product)
    {
        if(store.ActiveEvent != null)
        {
            ShowNotice("イベント中は商品情報を変更できません。");
            return;
        }

        editingProductID = product == null ? null : product.productID;
        ShowPage(Page.ProductForm);
        if(product != null)
        {
            productNameInput.text = product.productName ?? string.Empty;
            productPriceInput.text = GetDisplayPrice(product).ToString();
            productStockInput.text = product.initialStock.ToString();
            productImagePathInput.text = product.imagePath ?? string.Empty;
        }
    }

    private void SaveProductForm()
    {
        if(productNameInput == null || string.IsNullOrWhiteSpace(productNameInput.text))
        {
            ShowNotice("商品名を入力してください。");
            return;
        }

        if(!int.TryParse(productPriceInput.text, out int price) || price < 0)
        {
            ShowNotice("価格は0以上の数字で入力してください。");
            return;
        }

        if(!int.TryParse(productStockInput.text, out int stock) || stock < 0)
        {
            ShowNotice("在庫数は0以上の数字で入力してください。");
            return;
        }

        bool saved;
        string error;
        if(string.IsNullOrEmpty(editingProductID))
        {
            var product = new ProductData
            {
                productID = Guid.NewGuid().ToString("N"),
                productName = productNameInput.text.Trim(),
                description = string.Empty,
                DefaultPrice = price,
                ActivePrice = price,
                publishedDate = DateTime.Now.ToString("yyyy-MM-dd"),
                orderdQuantity = stock,
                initialStock = stock,
                reorderThreshold = 0,
                imagePath = productImagePathInput.text.Trim(),
                distributionType = DistributionType.Normal,
                isR18 = false,
                isSetOnly = false,
                isOneOfAKind = false,
                variationProductIDs = new List<string>(),
                setProductIDs = new List<string>(),
                notes = string.Empty
            };
            saved = catalog.TryAddProduct(product, out error);
        }
        else
        {
            saved = catalog.TryUpdateProduct(
                editingProductID,
                productNameInput.text,
                price,
                stock,
                productImagePathInput.text,
                out error);
        }

        if(!saved)
        {
            ShowNotice(error);
            return;
        }

        editingProductID = null;
        ShowPage(Page.Products);
    }

    private void ShowNotice(string message)
    {
        if(currentPage == Page.Payment)
        {
            ShowPage(Page.Payment);
        }
        else if(currentPage == Page.ProductForm)
        {
            string keepID = editingProductID;
            string name = productNameInput == null ? string.Empty : productNameInput.text;
            string price = productPriceInput == null ? string.Empty : productPriceInput.text;
            string stock = productStockInput == null ? string.Empty : productStockInput.text;
            string imagePath = productImagePathInput == null ? string.Empty : productImagePathInput.text;
            ShowPage(Page.ProductForm);
            editingProductID = keepID;
            productNameInput.text = name;
            productPriceInput.text = price;
            productStockInput.text = stock;
            productImagePathInput.text = imagePath;
        }
        else
        {
            ShowPage(currentPage);
        }

        if(!string.IsNullOrEmpty(message))
        {
            Text banner = CreateLabel(body, message, 15, Accent, FontStyle.Bold, 54);
            banner.transform.SetSiblingIndex(0);
            Debug.LogWarning(message);
        }
    }

    private long GetCartTotal()
    {
        long total = 0;
        foreach(CartItem item in cart)
        {
            if(item != null && item.quantity > 0)
            {
                total += (long)item.unitPrice * item.quantity;
            }
        }

        return total;
    }

    private int CartItemCount()
    {
        int count = 0;
        foreach(CartItem item in cart)
        {
            if(item != null)
            {
                count += item.quantity;
            }
        }

        return count;
    }

    private void ConfigureVerticalPage(int spacing)
    {
        VerticalLayoutGroup layout = body.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 8, 8);
        layout.spacing = spacing;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
    }

    private void ConfigureVertical(RectTransform target, int spacing, int inset)
    {
        VerticalLayoutGroup layout = target.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(inset, inset, inset, inset);
        layout.spacing = spacing;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
    }

    private RectTransform CreateRow(Transform parent, int spacing, Color? background = null)
    {
        RectTransform row = CreateRect("Row", parent);
        if(background.HasValue)
        {
            row.gameObject.AddComponent<Image>().color = background.Value;
        }

        HorizontalLayoutGroup layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 5, 5);
        layout.spacing = spacing;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = 62;
        return row;
    }

    private RectTransform CreateStack(Transform parent, Color background, int spacing, int horizontalInset, int verticalInset)
    {
        RectTransform stack = CreateRect("Card", parent);
        stack.gameObject.AddComponent<Image>().color = background;
        VerticalLayoutGroup layout = stack.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(horizontalInset, horizontalInset, verticalInset, verticalInset);
        layout.spacing = spacing;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        stack.gameObject.AddComponent<LayoutElement>().preferredHeight = 96;
        return stack;
    }

    private RectTransform CreateScroll(Transform parent, string name, float flexibleHeight)
    {
        RectTransform viewport = CreateRect(name, parent);
        viewport.gameObject.AddComponent<Image>().color = Paper;
        Mask mask = viewport.gameObject.AddComponent<Mask>();
        mask.showMaskGraphic = false;
        ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;

        RectTransform content = CreateRect("Content", viewport);
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = new Vector2(1, 1);
        content.pivot = new Vector2(0.5f, 1);
        content.offsetMin = new Vector2(0, 0);
        content.offsetMax = new Vector2(0, 0);
        scroll.viewport = viewport;
        scroll.content = content;
        viewport.gameObject.AddComponent<LayoutElement>().flexibleHeight = flexibleHeight;
        return viewport;
    }

    private RectTransform CreateVerticalScroll(Transform parent, float flexibleHeight)
    {
        RectTransform viewport = CreateScroll(parent, "List", flexibleHeight);
        RectTransform content = viewport.Find("Content") as RectTransform;
        VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(3, 3, 4, 12);
        layout.spacing = 8;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return content;
    }

    private Button CreateButton(Transform parent, string label, Color background, Color textColor, float height, Action action)
    {
        RectTransform rect = CreateRect("Button", parent);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = background;
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = background;
        colors.highlightedColor = Color.Lerp(background, Color.white, 0.12f);
        colors.pressedColor = Color.Lerp(background, Ink, 0.10f);
        colors.disabledColor = new Color(background.r, background.g, background.b, 0.45f);
        button.colors = colors;
        if(action != null)
        {
            button.onClick.AddListener(() => action());
        }

        if(!string.IsNullOrEmpty(label))
        {
            Text buttonText = CreateText(rect, label, 17, textColor, TextAnchor.MiddleCenter, FontStyle.Bold);
            buttonText.raycastTarget = false;
        }
        if(height > 0)
        {
            rect.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
        }

        return button;
    }

    private Text CreateLabel(Transform parent, string value, int fontSize, Color color, FontStyle style, float height)
    {
        Text text = CreateText(parent, value, fontSize, color, TextAnchor.MiddleLeft, style);
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
        return text;
    }

    private Text CreateText(Transform parent, string value, int fontSize, Color color, TextAnchor alignment, FontStyle style)
    {
        RectTransform rect = CreateRect("Text", parent);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        Text text = rect.gameObject.AddComponent<Text>();
        text.font = uiFont;
        text.text = value ?? string.Empty;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.color = color;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.supportRichText = false;
        text.raycastTarget = false;
        return text;
    }

    private InputField CreateInput(Transform parent, string placeholderText, bool numeric)
    {
        RectTransform rect = CreateRect("Input", parent);
        rect.gameObject.AddComponent<Image>().color = Card;
        InputField input = rect.gameObject.AddComponent<InputField>();
        input.lineType = InputField.LineType.SingleLine;
        input.contentType = numeric ? InputField.ContentType.IntegerNumber : InputField.ContentType.Standard;
        input.characterLimit = 64;

        Text value = CreateText(rect, string.Empty, 18, Ink, TextAnchor.MiddleLeft, FontStyle.Normal);
        value.rectTransform.offsetMin = new Vector2(12, 5);
        value.rectTransform.offsetMax = new Vector2(-12, -5);
        Text placeholder = CreateText(rect, placeholderText, 16, Muted, TextAnchor.MiddleLeft, FontStyle.Italic);
        placeholder.rectTransform.offsetMin = new Vector2(12, 5);
        placeholder.rectTransform.offsetMax = new Vector2(-12, -5);
        input.textComponent = value;
        input.placeholder = placeholder;
        return input;
    }

    private Image CreateImage(Transform parent, Color color)
    {
        RectTransform rect = CreateRect("Image", parent);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        return image;
    }

    private RectTransform CreatePanel(string name, Transform parent, Color color, float height)
    {
        RectTransform rect = CreateRect(name, parent);
        rect.gameObject.AddComponent<Image>().color = color;
        LayoutElement element = rect.gameObject.AddComponent<LayoutElement>();
        if(height > 0)
        {
            element.preferredHeight = height;
        }

        return rect;
    }

    private RectTransform CreateRect(string name, Transform parent)
    {
        var item = new GameObject(name, typeof(RectTransform));
        item.transform.SetParent(parent, false);
        RectTransform rect = item.GetComponent<RectTransform>();
        rect.localScale = Vector3.one;
        return rect;
    }

    private Sprite LoadProductSprite(string path)
    {
        if(string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return Resources.Load<Sprite>(path.Trim().Replace('\\', '/'));
    }

    private string GetProductInitial(string productName)
    {
        return string.IsNullOrWhiteSpace(productName) ? "商品" : productName.Trim().Substring(0, 1);
    }

    private int GetDisplayPrice(ProductData product)
    {
        if(product.distributionType == DistributionType.Free)
        {
            return 0;
        }

        return product.ActivePrice > 0 ? product.ActivePrice : Mathf.Max(0, product.DefaultPrice);
    }

    private void ClearChildren(Transform target)
    {
        for(int index = target.childCount - 1; index >= 0; index--)
        {
            Transform child = target.GetChild(index);
            child.SetParent(null, false);
            Destroy(child.gameObject);
        }
    }
}

