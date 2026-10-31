using System;
using System.Collections.Generic;
using System.IO;
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
        RegisteredEvents,
        RegisteredEventForm,
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
    private readonly HashSet<string> selectedEventProductIDs = new HashSet<string>();
    private readonly Dictionary<string, Sprite> productImageCache = new Dictionary<string, Sprite>();
    private string editingProductID;
    private string editingRegisteredEventID;
    private InputField eventNameInput;
    private InputField productNameInput;
    private InputField productPriceInput;
    private InputField productStockInput;
    private InputField setDiscountQuantityInput;
    private InputField setDiscountPriceInput;
    private InputField setDiscountGroupInput;
    private InputField bonusThresholdInput;
    private InputField bonusQuantityInput;
    private string selectedProductImagePath = string.Empty;
    private Image productImagePreview;
    private RectTransform imagePickerOverlay;
    private RectTransform confirmationOverlay;
    private Sprite sampleProductSprite;
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
        ShowPage(Page.Home);
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
            case Page.RegisteredEvents:
                BuildRegisteredEvents();
                BuildRegisteredEventsFooter();
                break;
            case Page.RegisteredEventForm:
                BuildRegisteredEventForm();
                BuildRegisteredEventFormFooter();
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
        // ShowPage rebuilds the header contents but keeps the header object itself.
        // Reuse its layout component on later page changes instead of adding a
        // duplicate (Unity rejects duplicate layout groups and returns null).
        HorizontalLayoutGroup row = GetOrAddComponent<HorizontalLayoutGroup>(header.gameObject);
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
        CreateLabel(body, store.ActiveEvent == null ? "ホーム" : "イベント会計中", 28, Ink, FontStyle.Bold, 46);
        CreateLabel(body, "商品・イベントの準備、売上履歴の確認ができます。会計はオフラインで利用できます。", 16, Muted, FontStyle.Normal, 68);

        if(store.ActiveEvent != null)
        {
            CreateLabel(body, $"開催中：{store.ActiveEvent.eventName}", 22, Primary, FontStyle.Bold, 54);
            CreateLabel(body, store.IsFinalizing
                ? "イベント終了処理を再開できます。"
                : "イベント中の在庫は仮の数として記録され、終了時に確定します。", 16, Muted, FontStyle.Normal, 54);
        }

        if(!string.IsNullOrEmpty(store.LoadError))
        {
            CreateLabel(body, store.LoadError, 15, Accent, FontStyle.Bold, 70);
        }

        int productCount = catalog.products == null ? 0 : catalog.products.Count;
        int registeredCount = 0;
        foreach(RegisteredEventDefinition definition in store.RegisteredEvents)
        {
            if(definition != null && !definition.isArchived)
            {
                registeredCount++;
            }
        }
        CreateLabel(body, $"登録商品：{productCount}点　／　開始できる登録イベント：{registeredCount}件", 18, Ink, FontStyle.Bold, 40);
        CreateLabel(body, "イベントごとに販売商品を選んで登録しておくと、開始時にその商品だけを会計へ表示します。", 16, Muted, FontStyle.Normal, 62);
    }

    private void BuildHomeFooter()
    {
        ConfigureVertical(footer, 8, 8);
        if(store.ActiveEvent != null)
        {
            CreateButton(footer, store.IsFinalizing ? "イベント終了処理を再開" : "会計へ戻る", Primary, Color.white, 58,
                () => ShowPage(store.IsFinalizing ? Page.EndEvent : Page.Register));
        }
        else
        {
            Button start = CreateButton(footer, "登録イベントから会計を開始", Primary, Color.white, 58, () => ShowPage(Page.RegisteredEvents));
            start.interactable = string.IsNullOrEmpty(store.LoadError);
        }

        RectTransform row = CreateRow(footer, 8);
        CreateButton(row, "イベント登録", Card, Ink, 48, () => ShowPage(Page.RegisteredEvents));
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
        string offers = string.Empty;
        if(product.setDiscountQuantity >= 2)
        {
            offers = string.IsNullOrWhiteSpace(product.setDiscountGroup)
                ? $"{product.setDiscountQuantity}点 {product.setDiscountPrice:N0}円"
                : $"{product.setDiscountGroup}組合せ {product.setDiscountQuantity}点 {product.setDiscountPrice:N0}円";
        }
        if(product.bonusThresholdAmount > 0 && product.bonusQuantity > 0)
        {
            offers = string.IsNullOrEmpty(offers)
                ? $"{product.bonusThresholdAmount:N0}円以上でおまけ"
                : offers + "\nおまけ付き";
        }
        string detailText = string.IsNullOrEmpty(offers)
            ? $"{price:N0}円\n残り {remaining}"
            : $"{price:N0}円　残り {remaining}\n{offers}";
        Text details = CreateText(button.transform, detailText, 12, remaining > 0 ? Muted : Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
        details.raycastTarget = false;
        details.gameObject.AddComponent<LayoutElement>().preferredHeight = 40;
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
        string giftStockWarning = GetGiftStockWarning();
        if(!string.IsNullOrEmpty(giftStockWarning))
        {
            CreateLabel(body, giftStockWarning, 14, Accent, FontStyle.Bold, 40);
        }

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
        string lineDescription = item.isBonus
            ? $"おまけ　{item.productName}\n無料 × {item.quantity}"
            : $"{item.productName}\n{item.unitPrice:N0}円 × {item.quantity}";
        if(!item.isBonus)
        {
            RegisterProductSnapshot snapshot = store.ActiveEvent.products.Find(product => product != null && product.productID == item.productID);
            if(snapshot != null && !string.IsNullOrWhiteSpace(snapshot.setDiscountGroup) && store.IsGroupSetDiscountActive(cart, item.productID))
            {
                lineDescription = $"{item.productName}\n「{snapshot.setDiscountGroup}」グループ割引対象";
                row.GetComponent<LayoutElement>().preferredHeight = 72;
            }
            else if(snapshot != null && string.IsNullOrWhiteSpace(snapshot.setDiscountGroup) && snapshot.setDiscountQuantity >= 2 && item.quantity >= snapshot.setDiscountQuantity)
            {
                lineDescription = $"{item.productName}\nセット割引　{store.CalculateCartItemTotal(item.productID, item.quantity):N0}円";
                row.GetComponent<LayoutElement>().preferredHeight = 72;
            }
        }
        Text title = CreateText(row, lineDescription, 15, item.isBonus ? Primary : Ink, TextAnchor.MiddleLeft, FontStyle.Bold);
        title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        if(!item.isBonus)
        {
            CreateButton(row, "−", AccentLight, Ink, 48, () => ChangeCartQuantity(item.productID, -1)).gameObject.GetComponent<LayoutElement>().preferredWidth = 54;
            CreateButton(row, "+", PrimaryLight, Ink, 48, () => ChangeCartQuantity(item.productID, 1)).gameObject.GetComponent<LayoutElement>().preferredWidth = 54;
        }
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
        RectTransform form = CreateVerticalScroll(body, 1);
        bool editing = !string.IsNullOrEmpty(editingProductID);
        CreateLabel(form, editing ? "商品を編集" : "商品を登録", 25, Ink, FontStyle.Bold, 44);
        CreateLabel(form, "価格・在庫数はイベント会計を始める時点で記録されます。", 15, Muted, FontStyle.Normal, 42);

        CreateLabel(form, "商品名", 15, Ink, FontStyle.Bold, 24);
        productNameInput = CreateInput(form, "例：新刊イラスト集", false);
        productNameInput.gameObject.AddComponent<LayoutElement>().preferredHeight = 54;

        CreateLabel(form, "価格（円）", 15, Ink, FontStyle.Bold, 24);
        productPriceInput = CreateInput(form, "例：700", true);
        productPriceInput.gameObject.AddComponent<LayoutElement>().preferredHeight = 54;

        CreateLabel(form, "在庫数", 15, Ink, FontStyle.Bold, 24);
        productStockInput = CreateInput(form, "例：20", true);
        productStockInput.gameObject.AddComponent<LayoutElement>().preferredHeight = 54;

        CreateLabel(form, "商品画像", 15, Ink, FontStyle.Bold, 24);
        RectTransform previewRow = CreateRow(form, 8, Card);
        productImagePreview = CreateImage(previewRow, PrimaryLight);
        productImagePreview.preserveAspect = true;
        productImagePreview.gameObject.AddComponent<LayoutElement>().preferredWidth = 66;
        productImagePreview.gameObject.GetComponent<LayoutElement>().preferredHeight = 66;
        Text imageLabel = CreateLabel(previewRow, "画像を選択してください", 14, Muted, FontStyle.Normal, 48);
        imageLabel.GetComponent<LayoutElement>().flexibleWidth = 1;
        CreateButton(form, "アップロードした画像一覧", PrimaryLight, Ink, 48, OpenImageGallery);
        CreateButton(form, "端末からアップロードする", Primary, Color.white, 52, UploadProductImage);
        CreateButton(form, "てきとうなサンプル画像にする", Card, Ink, 48, UseSampleProductImage);

        CreateLabel(form, "セット割引グループ（同じ名前の商品を合算・任意）", 15, Ink, FontStyle.Bold, 32);
        setDiscountGroupInput = CreateInput(form, "例：ポストカード", false);
        setDiscountGroupInput.gameObject.AddComponent<LayoutElement>().preferredHeight = 50;
        CreateLabel(form, "同じグループの商品は、通常価格・セット点数・セット合計を揃えてください。", 13, Muted, FontStyle.Normal, 38);
        CreateLabel(form, "セット販売条件（任意）", 15, Ink, FontStyle.Bold, 30);
        RectTransform setRow = CreateRow(form, 8);
        setDiscountQuantityInput = CreateInput(setRow, "セット点数", true);
        setDiscountPriceInput = CreateInput(setRow, "セット合計円", true);
        setDiscountQuantityInput.gameObject.AddComponent<LayoutElement>().preferredHeight = 52;
        setDiscountPriceInput.gameObject.AddComponent<LayoutElement>().preferredHeight = 52;

        CreateLabel(form, "購入金額のおまけ（任意）", 15, Ink, FontStyle.Bold, 30);
        RectTransform bonusRow = CreateRow(form, 8);
        bonusThresholdInput = CreateInput(bonusRow, "購入金額円以上", true);
        bonusQuantityInput = CreateInput(bonusRow, "この商品を何点", true);
        bonusThresholdInput.gameObject.AddComponent<LayoutElement>().preferredHeight = 52;
        bonusQuantityInput.gameObject.AddComponent<LayoutElement>().preferredHeight = 52;
        CreateLabel(form, "購入金額はセット割引前の小計です。おまけの在庫も会計時に確認します。", 13, Muted, FontStyle.Normal, 38);
        RefreshProductImagePreview();
    }

    private void BuildProductFormFooter()
    {
        ConfigureVertical(footer, 8, 8);
        CreateButton(footer, "保存する", Primary, Color.white, 58, SaveProductForm);
        CreateButton(footer, "商品一覧へ戻る", Card, Ink, 48, () => ShowPage(Page.Products));
    }

    private void BuildRegisteredEvents()
    {
        ConfigureVerticalPage(8);
        CreateLabel(body, "登録イベント", 26, Ink, FontStyle.Bold, 46);
        CreateLabel(body, "イベント名と販売商品を登録します。開始前に内容を確認できます。", 15, Muted, FontStyle.Normal, 44);
        RectTransform list = CreateVerticalScroll(body, 1);
        int availableCount = 0;
        foreach(RegisteredEventDefinition definition in store.RegisteredEvents)
        {
            if(definition == null || definition.isArchived)
            {
                continue;
            }

            availableCount++;
            RectTransform card = CreateStack(list, Card, 8, 10, 8);
            CreateLabel(card, definition.eventName, 18, Ink, FontStyle.Bold, 34);
            CreateLabel(card, $"販売商品 {definition.productIDs.Count}点：{GetRegisteredProductNames(definition)}", 14, Muted, FontStyle.Normal,
                Mathf.Max(38, definition.productIDs.Count * 22));
            RectTransform actions = CreateRow(card, 8);
            CreateButton(actions, "開始", Primary, Color.white, 48, () => ConfirmStartEvent(definition));
            CreateButton(actions, "編集", Card, Ink, 48, () => OpenRegisteredEventForm(definition));
            card.GetComponent<LayoutElement>().preferredHeight = 142 + definition.productIDs.Count * 22;
        }

        if(availableCount == 0)
        {
            CreateLabel(list, "開始できる登録イベントはありません。下のボタンから作成してください。", 16, Muted, FontStyle.Normal, 64);
        }
    }

    private void BuildRegisteredEventsFooter()
    {
        ConfigureVertical(footer, 8, 8);
        CreateButton(footer, "＋ 登録イベントを作成", Primary, Color.white, 56, () => OpenRegisteredEventForm(null));
        CreateButton(footer, "ホームへ戻る", Card, Ink, 48, () => ShowPage(Page.Home));
    }

    private string GetRegisteredProductNames(RegisteredEventDefinition definition)
    {
        var names = new List<string>();
        if(definition != null && definition.productIDs != null && catalog.products != null)
        {
            foreach(string productID in definition.productIDs)
            {
                ProductData product = catalog.products.Find(item => item != null && item.productID == productID);
                names.Add(product == null ? "（商品なし）" : product.productName);
            }
        }

        return string.Join("、", names.ToArray());
    }

    private void BuildRegisteredEventForm()
    {
        ConfigureVerticalPage(8);
        RectTransform form = CreateVerticalScroll(body, 1);
        bool editing = !string.IsNullOrEmpty(editingRegisteredEventID);
        CreateLabel(form, editing ? "登録イベントを編集" : "登録イベントを作成", 25, Ink, FontStyle.Bold, 44);
        CreateLabel(form, "イベント名", 15, Ink, FontStyle.Bold, 24);
        eventNameInput = CreateInput(form, "例：北ティア24", false);
        eventNameInput.gameObject.AddComponent<LayoutElement>().preferredHeight = 54;
        CreateLabel(form, "このイベントで販売する商品を選択", 16, Ink, FontStyle.Bold, 34);

        if(catalog.products == null || catalog.products.Count == 0)
        {
            CreateLabel(form, "商品がありません。先に商品管理から商品を登録してください。", 15, Accent, FontStyle.Bold, 56);
            return;
        }

        foreach(ProductData product in catalog.products)
        {
            if(product == null)
            {
                continue;
            }

            bool selected = selectedEventProductIDs.Contains(product.productID);
            string mark = selected ? "✓" : "　";
            string label = $"{mark}　{product.productName}　{GetDisplayPrice(product):N0}円　／　在庫 {product.initialStock}";
            CreateButton(form, label, selected ? PrimaryLight : Card, Ink, 54, () =>
            {
                string eventName = eventNameInput == null ? string.Empty : eventNameInput.text;
                if(selectedEventProductIDs.Contains(product.productID))
                {
                    selectedEventProductIDs.Remove(product.productID);
                }
                else
                {
                    selectedEventProductIDs.Add(product.productID);
                }

                ShowPage(Page.RegisteredEventForm);
                eventNameInput.text = eventName;
            });
        }
    }

    private void BuildRegisteredEventFormFooter()
    {
        ConfigureVertical(footer, 8, 8);
        CreateButton(footer, "登録する", Primary, Color.white, 56, SaveRegisteredEventForm);
        CreateButton(footer, "一覧へ戻る", Card, Ink, 48, () => ShowPage(Page.RegisteredEvents));
    }

    private void OpenRegisteredEventForm(RegisteredEventDefinition definition)
    {
        if(store.ActiveEvent != null)
        {
            ShowNotice("会計を終了してから登録イベントを編集してください。");
            return;
        }

        if(!EnsureCatalogProductIDs())
        {
            return;
        }

        editingRegisteredEventID = definition == null ? null : definition.registeredEventID;
        selectedEventProductIDs.Clear();
        if(definition != null && definition.productIDs != null)
        {
            selectedEventProductIDs.UnionWith(definition.productIDs);
        }

        ShowPage(Page.RegisteredEventForm);
        eventNameInput.text = definition == null ? string.Empty : definition.eventName;
    }

    private bool EnsureCatalogProductIDs()
    {
        if(catalog.products == null)
        {
            catalog.products = new List<ProductData>();
        }

        bool changed = false;
        foreach(ProductData product in catalog.products)
        {
            if(product != null && string.IsNullOrWhiteSpace(product.productID))
            {
                product.productID = Guid.NewGuid().ToString("N");
                changed = true;
            }
        }

        if(!changed)
        {
            return true;
        }

        try
        {
            catalog.SaveProducts();
            return true;
        }
        catch(Exception exception)
        {
            ShowNotice("商品IDを保存できませんでした：" + exception.Message);
            return false;
        }
    }

    private void SaveRegisteredEventForm()
    {
        var definition = new RegisteredEventDefinition
        {
            registeredEventID = editingRegisteredEventID,
            eventName = eventNameInput == null ? string.Empty : eventNameInput.text.Trim(),
            productIDs = new List<string>()
        };

        foreach(ProductData product in catalog.products)
        {
            if(product != null && selectedEventProductIDs.Contains(product.productID))
            {
                definition.productIDs.Add(product.productID);
            }
        }

        if(!store.SaveRegisteredEvent(definition, out string error))
        {
            ShowNotice(error);
            return;
        }

        editingRegisteredEventID = null;
        selectedEventProductIDs.Clear();
        ShowPage(Page.RegisteredEvents);
    }

    private void ConfirmStartEvent(RegisteredEventDefinition definition)
    {
        if(store.ActiveEvent != null)
        {
            ShowNotice("すでにイベント会計中です。");
            return;
        }

        ShowConfirmationDialog("イベントを開始しますか？",
            $"{definition.eventName}\n販売商品 {definition.productIDs.Count}点を会計画面に表示します。",
            "開始する", () =>
            {
                if(!store.StartEvent(definition, catalog, out string error))
                {
                    ShowNotice(error);
                    return;
                }

                cart.Clear();
                ShowPage(Page.Register);
            });
    }

    private void BuildHistory()
    {
        ConfigureVerticalPage(8);
        CreateLabel(body, "売上履歴", 26, Ink, FontStyle.Bold, 46);
        RectTransform list = CreateVerticalScroll(body, 1);

        if(store.ActiveEvent != null)
        {
            CreateEventHistoryCard(list, store.ActiveEvent, "開催中・仮集計", false);
        }

        IReadOnlyList<RegisterEventRecord> completed = store.CompletedEvents;
        if(completed.Count == 0 && store.ActiveEvent == null)
        {
            CreateLabel(list, "イベント履歴はまだありません。", 17, Muted, FontStyle.Normal, 48);
            return;
        }

        foreach(RegisterEventRecord record in completed)
        {
            CreateEventHistoryCard(list, record, "アーカイブ", true);
        }
    }

    private void CreateEventHistoryCard(Transform parent, RegisterEventRecord record, string status, bool allowManagement)
    {
        RectTransform card = CreateStack(parent, Card, 10, 10, 10);
        CreateLabel(card, $"{record.eventName}　{status}{(record.isLocked ? "（ロック中）" : string.Empty)}", 18, Ink, FontStyle.Bold, 34);
        CreateLabel(card, $"売上 {store.GetEventSalesTotal(record):N0}円　・　頒布 {store.GetEventItemCount(record)}点　・　会計 {record.sales.Count}回", 15, Muted, FontStyle.Normal, 30);
        CreateLabel(card, $"使用したおつり（返した額）　{store.GetEventChangeTotal(record):N0}円", 15, Primary, FontStyle.Bold, 28);

        var inventoryLines = new List<string>();
        if(record.products != null)
        {
            foreach(RegisterProductSnapshot snapshot in record.products)
            {
                if(snapshot == null)
                {
                    continue;
                }

                long sold = store.GetProductSold(record, snapshot.productID);
                if(sold > 0)
                {
                    long ending = Math.Max(0L, snapshot.startingStock - sold);
                    inventoryLines.Add($"{snapshot.productName}　在庫 {snapshot.startingStock} → {ending}（−{sold}点）");
                }
            }
        }
        CreateLabel(card, inventoryLines.Count == 0 ? "在庫変動なし" : string.Join("\n", inventoryLines.ToArray()), 14, Muted, FontStyle.Normal,
            Mathf.Max(24, inventoryLines.Count * 24));

        if(!string.IsNullOrEmpty(record.startedAt))
        {
            DateTime started;
            if(DateTime.TryParse(record.startedAt, out started))
            {
                CreateLabel(card, started.ToString("yyyy/MM/dd HH:mm"), 13, Muted, FontStyle.Normal, 22);
            }
        }

        if(allowManagement)
        {
            RectTransform actions = CreateRow(card, 8);
            Button lockButton = CreateButton(actions, record.isLocked ? "ロック解除" : "ロック", record.isLocked ? WarningLight : PrimaryLight, Ink, 48,
                () => ToggleHistoryLock(record));
            lockButton.interactable = !record.deletionPending;
            Button delete = CreateButton(actions, record.deletionPending ? "在庫戻しを再開" : "削除", record.isLocked && !record.deletionPending ? Card : AccentLight, record.isLocked && !record.deletionPending ? Muted : Accent, 48,
                () => ConfirmDeleteHistory(record));
            delete.interactable = record.deletionPending || !record.isLocked;
        }

        card.GetComponent<LayoutElement>().preferredHeight = 190 + inventoryLines.Count * 24 + (allowManagement ? 70 : 0);
    }

    private void BuildHistoryFooter()
    {
        ConfigureVertical(footer, 8, 8);
        CreateButton(footer, store.ActiveEvent == null ? "ホームへ戻る" : "会計へ戻る", Card, Ink, 56,
            () => ShowPage(store.ActiveEvent == null ? Page.Home : Page.Register));
    }

    private void ToggleHistoryLock(RegisterEventRecord record)
    {
        if(!store.SetCompletedEventLocked(record.eventID, !record.isLocked, out string error))
        {
            ShowNotice(error);
            return;
        }

        ShowPage(Page.History);
    }

    private void ConfirmDeleteHistory(RegisterEventRecord record)
    {
        if(record.deletionPending)
        {
            DeleteHistory(record);
            return;
        }

        ShowConfirmationDialog("売上履歴を削除しますか？",
            $"「{record.eventName}」の履歴を削除します。販売数分の在庫を商品へ戻し、登録イベントを再度使える状態にします。この操作は取り消せません。",
            "削除する", () =>
            {
                DeleteHistory(record);
            });
    }

    private void DeleteHistory(RegisterEventRecord record)
    {
        if(!store.DeleteCompletedEvent(record.eventID, catalog, out string error))
        {
            ShowNotice(error);
            return;
        }

        ShowPage(Page.History);
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

    private void AddToCart(ProductData product)
    {
        if(store.ActiveEvent == null || product == null)
        {
            return;
        }

        int available = store.GetRemainingStock(product.productID);
        CartItem item = cart.Find(candidate => !candidate.isBonus && candidate.productID == product.productID);
        int inCart = 0;
        foreach(CartItem cartItem in cart)
        {
            if(cartItem != null && cartItem.productID == product.productID)
            {
                inCart += cartItem.quantity;
            }
        }
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
        RefreshCartBonuses();
        ShowPage(Page.Register);
    }

    private void ChangeCartQuantity(string productID, int delta)
    {
        CartItem item = cart.Find(candidate => !candidate.isBonus && candidate.productID == productID);
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

        RefreshCartBonuses();
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
        selectedProductImagePath = product == null ? string.Empty : product.imagePath ?? string.Empty;
        ShowPage(Page.ProductForm);
        if(product != null)
        {
            productNameInput.text = product.productName ?? string.Empty;
            productPriceInput.text = GetDisplayPrice(product).ToString();
            productStockInput.text = product.initialStock.ToString();
            setDiscountQuantityInput.text = product.setDiscountQuantity > 0 ? product.setDiscountQuantity.ToString() : string.Empty;
            setDiscountPriceInput.text = product.setDiscountPrice > 0 ? product.setDiscountPrice.ToString() : string.Empty;
            setDiscountGroupInput.text = product.setDiscountGroup ?? string.Empty;
            bonusThresholdInput.text = product.bonusThresholdAmount > 0 ? product.bonusThresholdAmount.ToString() : string.Empty;
            bonusQuantityInput.text = product.bonusQuantity > 0 ? product.bonusQuantity.ToString() : string.Empty;
        }
        RefreshProductImagePreview();
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

        if(!TryParseOptionalNonNegative(setDiscountQuantityInput.text, out int setQuantity) ||
            !TryParseOptionalNonNegative(setDiscountPriceInput.text, out int setPrice) ||
            !TryParseOptionalNonNegative(bonusThresholdInput.text, out int bonusThreshold) ||
            !TryParseOptionalNonNegative(bonusQuantityInput.text, out int bonusCount))
        {
            ShowNotice("セット割引とおまけの条件には0以上の数字を入力してください。");
            return;
        }

        if((setQuantity == 0) != (setPrice == 0) || (setQuantity > 0 && (setQuantity < 2 || setPrice <= 0 || setPrice >= (long)price * setQuantity)))
        {
            ShowNotice("セット割引は2点以上、通常価格より安いセット合計を設定してください。");
            return;
        }

        string setGroup = setDiscountGroupInput.text.Trim();
        if(setQuantity > 0 && price == 0)
        {
            ShowNotice("無料商品のセット割引は設定できません。");
            return;
        }

        if((bonusThreshold == 0) != (bonusCount == 0))
        {
            ShowNotice("おまけは購入金額と付属数の両方を入力してください。");
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
                imagePath = selectedProductImagePath,
                setDiscountQuantity = setQuantity,
                setDiscountPrice = setPrice,
                setDiscountGroup = setGroup,
                bonusThresholdAmount = bonusThreshold,
                bonusQuantity = bonusCount,
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
                selectedProductImagePath,
                setQuantity,
                setPrice,
                setGroup,
                bonusThreshold,
                bonusCount,
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

    private static bool TryParseOptionalNonNegative(string value, out int number)
    {
        if(string.IsNullOrWhiteSpace(value))
        {
            number = 0;
            return true;
        }

        return int.TryParse(value, out number) && number >= 0;
    }

    private string UploadedImageDirectory => Path.Combine(Application.persistentDataPath, "ProductImages");

    private void UploadProductImage()
    {
        NativeGallery.GetImageFromGallery(path =>
        {
            if(string.IsNullOrEmpty(path))
            {
                return;
            }

            try
            {
                Texture2D texture = NativeGallery.LoadImageAtPath(path, 1024, false, false);
                if(texture == null)
                {
                    ShowNotice("画像を読み込めませんでした。別の画像を選んでください。");
                    return;
                }

                Directory.CreateDirectory(UploadedImageDirectory);
                string fileName = Guid.NewGuid().ToString("N") + ".png";
                File.WriteAllBytes(Path.Combine(UploadedImageDirectory, fileName), texture.EncodeToPNG());
                Destroy(texture);
                selectedProductImagePath = "upload:" + fileName;
                RefreshProductImagePreview();
            }
            catch(Exception exception)
            {
                ShowNotice("画像を保存できませんでした：" + exception.Message);
            }
        }, "商品画像を選択", "image/*");
    }

    private void UseSampleProductImage()
    {
        selectedProductImagePath = "sample:cover";
        RefreshProductImagePreview();
    }

    private void RefreshProductImagePreview()
    {
        if(productImagePreview == null)
        {
            return;
        }

        Sprite sprite = LoadProductSprite(selectedProductImagePath);
        productImagePreview.sprite = sprite;
        productImagePreview.color = sprite == null ? PrimaryLight : Color.white;
    }

    private void OpenImageGallery()
    {
        if(imagePickerOverlay != null)
        {
            Destroy(imagePickerOverlay.gameObject);
        }

        imagePickerOverlay = CreateRect("Uploaded Image Gallery", canvas.transform);
        imagePickerOverlay.anchorMin = Vector2.zero;
        imagePickerOverlay.anchorMax = Vector2.one;
        imagePickerOverlay.offsetMin = Vector2.zero;
        imagePickerOverlay.offsetMax = Vector2.zero;
        Image shade = imagePickerOverlay.gameObject.AddComponent<Image>();
        shade.color = new Color(0, 0, 0, 0.55f);

        RectTransform panel = CreateRect("Gallery Panel", imagePickerOverlay);
        panel.anchorMin = new Vector2(0.06f, 0.08f);
        panel.anchorMax = new Vector2(0.94f, 0.92f);
        panel.offsetMin = Vector2.zero;
        panel.offsetMax = Vector2.zero;
        panel.gameObject.AddComponent<Image>().color = Paper;
        VerticalLayoutGroup layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 14, 14);
        layout.spacing = 8;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        CreateLabel(panel, "アップロードした画像一覧", 22, Ink, FontStyle.Bold, 42);

        RectTransform list = CreateVerticalScroll(panel, 1);
        string[] imageFiles = Directory.Exists(UploadedImageDirectory)
            ? Directory.GetFiles(UploadedImageDirectory, "*.png")
            : new string[0];
        Array.Sort(imageFiles, StringComparer.OrdinalIgnoreCase);
        if(imageFiles.Length == 0)
        {
            CreateLabel(list, "アップロード済みの画像はありません。端末から画像を選ぶか、サンプル画像を使ってください。", 16, Muted, FontStyle.Normal, 78);
        }
        else
        {
            foreach(string filePath in imageFiles)
            {
                string fileName = Path.GetFileName(filePath);
                Button imageButton = CreateButton(list, string.Empty, Card, Ink, 76, () =>
                {
                    selectedProductImagePath = "upload:" + fileName;
                    Destroy(imagePickerOverlay.gameObject);
                    imagePickerOverlay = null;
                    RefreshProductImagePreview();
                });
                HorizontalLayoutGroup row = imageButton.gameObject.AddComponent<HorizontalLayoutGroup>();
                row.padding = new RectOffset(8, 8, 6, 6);
                row.spacing = 10;
                row.childControlWidth = true;
                row.childControlHeight = true;
                row.childForceExpandWidth = false;
                row.childForceExpandHeight = true;
                Image thumbnail = CreateImage(imageButton.transform, Color.white);
                thumbnail.sprite = LoadProductSprite("upload:" + fileName);
                thumbnail.preserveAspect = true;
                thumbnail.gameObject.AddComponent<LayoutElement>().preferredWidth = 60;
                Text label = CreateText(imageButton.transform, fileName, 13, Ink, TextAnchor.MiddleLeft, FontStyle.Normal);
                label.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
                thumbnail.raycastTarget = false;
                label.raycastTarget = false;
            }
        }

        CreateButton(panel, "閉じる", Card, Ink, 50, () =>
        {
            Destroy(imagePickerOverlay.gameObject);
            imagePickerOverlay = null;
        });
    }

    private void ShowConfirmationDialog(string title, string message, string confirmLabel, Action confirmAction)
    {
        if(confirmationOverlay != null)
        {
            Destroy(confirmationOverlay.gameObject);
        }

        confirmationOverlay = CreateRect("Confirmation Dialog", canvas.transform);
        confirmationOverlay.anchorMin = Vector2.zero;
        confirmationOverlay.anchorMax = Vector2.one;
        confirmationOverlay.offsetMin = Vector2.zero;
        confirmationOverlay.offsetMax = Vector2.zero;
        Image shade = confirmationOverlay.gameObject.AddComponent<Image>();
        shade.color = new Color(0, 0, 0, 0.58f);

        RectTransform panel = CreateRect("Dialog Panel", confirmationOverlay);
        panel.anchorMin = new Vector2(0.07f, 0.28f);
        panel.anchorMax = new Vector2(0.93f, 0.72f);
        panel.offsetMin = Vector2.zero;
        panel.offsetMax = Vector2.zero;
        panel.gameObject.AddComponent<Image>().color = Paper;
        VerticalLayoutGroup layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(18, 18, 16, 16);
        layout.spacing = 10;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        CreateLabel(panel, title, 22, Ink, FontStyle.Bold, 42);
        CreateLabel(panel, message, 16, Muted, FontStyle.Normal, 92);
        RectTransform actions = CreateRow(panel, 8);
        CreateButton(actions, "戻る", Card, Ink, 54, CloseConfirmationDialog);
        CreateButton(actions, confirmLabel, Accent, Color.white, 54, () =>
        {
            CloseConfirmationDialog();
            confirmAction?.Invoke();
        });
    }

    private void CloseConfirmationDialog()
    {
        if(confirmationOverlay != null)
        {
            Destroy(confirmationOverlay.gameObject);
            confirmationOverlay = null;
        }
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
            string imagePath = selectedProductImagePath;
            string setQuantity = setDiscountQuantityInput == null ? string.Empty : setDiscountQuantityInput.text;
            string setPrice = setDiscountPriceInput == null ? string.Empty : setDiscountPriceInput.text;
            string setGroup = setDiscountGroupInput == null ? string.Empty : setDiscountGroupInput.text;
            string bonusThreshold = bonusThresholdInput == null ? string.Empty : bonusThresholdInput.text;
            string bonusQuantity = bonusQuantityInput == null ? string.Empty : bonusQuantityInput.text;
            ShowPage(Page.ProductForm);
            editingProductID = keepID;
            productNameInput.text = name;
            productPriceInput.text = price;
            productStockInput.text = stock;
            selectedProductImagePath = imagePath;
            setDiscountQuantityInput.text = setQuantity;
            setDiscountPriceInput.text = setPrice;
            setDiscountGroupInput.text = setGroup;
            bonusThresholdInput.text = bonusThreshold;
            bonusQuantityInput.text = bonusQuantity;
            RefreshProductImagePreview();
        }
        else if(currentPage == Page.RegisteredEventForm)
        {
            string eventName = eventNameInput == null ? string.Empty : eventNameInput.text;
            ShowPage(Page.RegisteredEventForm);
            eventNameInput.text = eventName;
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
        return store == null ? 0 : store.CalculateCartTotal(cart);
    }

    private void RefreshCartBonuses()
    {
        cart.RemoveAll(item => item == null || item.isBonus);
        RegisterEventRecord active = store.ActiveEvent;
        if(active == null || active.products == null)
        {
            return;
        }

        long paidSubtotal = 0;
        foreach(CartItem item in cart)
        {
            if(item != null && item.quantity > 0)
            {
                paidSubtotal += (long)item.unitPrice * item.quantity;
            }
        }

        foreach(RegisterProductSnapshot snapshot in active.products)
        {
            if(snapshot == null || snapshot.bonusThresholdAmount <= 0 || snapshot.bonusQuantity <= 0 || paidSubtotal < snapshot.bonusThresholdAmount)
            {
                continue;
            }

            int paidQuantity = 0;
            foreach(CartItem item in cart)
            {
                if(item != null && item.productID == snapshot.productID)
                {
                    paidQuantity += item.quantity;
                }
            }

            if(store.GetRemainingStock(snapshot.productID) < paidQuantity + snapshot.bonusQuantity)
            {
                continue;
            }

            cart.Add(new CartItem
            {
                productID = snapshot.productID,
                productName = snapshot.productName,
                unitPrice = 0,
                quantity = snapshot.bonusQuantity,
                isBonus = true
            });
        }
    }

    private string GetGiftStockWarning()
    {
        RegisterEventRecord active = store.ActiveEvent;
        if(active == null || active.products == null)
        {
            return string.Empty;
        }

        long paidSubtotal = 0;
        foreach(CartItem item in cart)
        {
            if(item != null && !item.isBonus)
            {
                paidSubtotal += (long)item.unitPrice * item.quantity;
            }
        }

        foreach(RegisterProductSnapshot snapshot in active.products)
        {
            if(snapshot == null || snapshot.bonusThresholdAmount <= 0 || snapshot.bonusQuantity <= 0 || paidSubtotal < snapshot.bonusThresholdAmount)
            {
                continue;
            }

            int paidQuantity = 0;
            bool alreadyAdded = false;
            foreach(CartItem item in cart)
            {
                if(item == null || item.productID != snapshot.productID)
                {
                    continue;
                }

                if(item.isBonus)
                {
                    alreadyAdded = true;
                }
                else
                {
                    paidQuantity += item.quantity;
                }
            }

            if(!alreadyAdded && store.GetRemainingStock(snapshot.productID) < paidQuantity + snapshot.bonusQuantity)
            {
                return $"{snapshot.productName}のおまけは、在庫不足のため今回付きません。";
            }
        }

        return string.Empty;
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
        VerticalLayoutGroup layout = GetOrAddComponent<VerticalLayoutGroup>(body.gameObject);
        layout.padding = new RectOffset(8, 8, 8, 8);
        layout.spacing = spacing;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
    }

    private void ConfigureVertical(RectTransform target, int spacing, int inset)
    {
        VerticalLayoutGroup layout = GetOrAddComponent<VerticalLayoutGroup>(target.gameObject);
        layout.padding = new RectOffset(inset, inset, inset, inset);
        layout.spacing = spacing;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
    }

    private static T GetOrAddComponent<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : target.AddComponent<T>();
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

        RectTransform track = CreateRect("Scroll Indicator", viewport);
        track.anchorMin = new Vector2(1, 0);
        track.anchorMax = new Vector2(1, 1);
        track.pivot = new Vector2(1, 0.5f);
        track.sizeDelta = new Vector2(5, -8);
        track.anchoredPosition = new Vector2(-2, 0);
        Image trackImage = track.gameObject.AddComponent<Image>();
        trackImage.color = new Color(0.78f, 0.79f, 0.77f, 0.8f);
        trackImage.raycastTarget = false;
        Scrollbar scrollbar = track.gameObject.AddComponent<Scrollbar>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scrollbar.targetGraphic = trackImage;
        scrollbar.interactable = false;
        RectTransform handle = CreateRect("Thumb", track);
        handle.anchorMin = Vector2.zero;
        handle.anchorMax = Vector2.one;
        handle.offsetMin = Vector2.zero;
        handle.offsetMax = Vector2.zero;
        Image handleImage = handle.gameObject.AddComponent<Image>();
        handleImage.color = Primary;
        handleImage.raycastTarget = false;
        scrollbar.handleRect = handle;
        scroll.verticalScrollbar = scrollbar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
        scroll.verticalScrollbarSpacing = -5;
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

        path = path.Trim();
        if(productImageCache.TryGetValue(path, out Sprite cachedSprite))
        {
            return cachedSprite;
        }

        if(path == "sample:cover")
        {
            sampleProductSprite = sampleProductSprite == null ? CreateSampleProductSprite() : sampleProductSprite;
            productImageCache[path] = sampleProductSprite;
            return sampleProductSprite;
        }

        if(path.StartsWith("upload:", StringComparison.Ordinal))
        {
            string fileName = Path.GetFileName(path.Substring("upload:".Length));
            string filePath = Path.Combine(UploadedImageDirectory, fileName);
            if(!File.Exists(filePath))
            {
                return null;
            }

            byte[] bytes = File.ReadAllBytes(filePath);
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if(!texture.LoadImage(bytes, true))
            {
                Destroy(texture);
                return null;
            }

            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            productImageCache[path] = sprite;
            return sprite;
        }

        return Resources.Load<Sprite>(path.Replace('\\', '/'));
    }

    private static Sprite CreateSampleProductSprite()
    {
        const int width = 256;
        const int height = 320;
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        var pixels = new Color[width * height];
        Color paper = new Color(0.96f, 0.91f, 0.80f);
        Color cover = new Color(0.42f, 0.64f, 0.62f);
        Color accent = new Color(0.91f, 0.56f, 0.46f);
        for(int y = 0; y < height; y++)
        {
            for(int x = 0; x < width; x++)
            {
                Color color = paper;
                if(x >= 25 && x <= 230 && y >= 18 && y <= 302)
                {
                    color = cover;
                    int dx = x - 128;
                    int dy = y - 166;
                    if(dx * dx + dy * dy < 55 * 55)
                    {
                        color = paper;
                    }
                    if(dx * dx + dy * dy < 34 * 34)
                    {
                        color = accent;
                    }
                    if(y < 62)
                    {
                        color = accent;
                    }
                }

                pixels[y * width + x] = color;
            }
        }

        texture.SetPixels(pixels);
        texture.Apply(false, true);
        return Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f));
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

