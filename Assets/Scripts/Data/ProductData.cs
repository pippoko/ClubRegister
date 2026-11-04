using System;
using System.Collections.Generic;

[Serializable]
public class ProductData
{
    // 商品の基本情報
    public string productID;
    public string productName;
    public string description;

    // 販売情報
    public int DefaultPrice;
    public int ActivePrice;
    public string publishedDate;

    // 制作・在庫情報
    public int orderdQuantity;
    public int initialStock;
    public int reorderThreshold;

    // 商品画像の参照先
    public string imagePath;

    // イベント開始時に記録する販売特典
    public int setDiscountQuantity;
    public int setDiscountPrice;
    public string setDiscountGroup;
    public int bonusThresholdAmount;
    public int bonusQuantity;

    // 頒布条件
    public DistributionType distributionType;
    public bool isR18;
    public bool isSetOnly;
    public bool isOneOfAKind;

    // 関連商品
    public List<string> variationProductIDs;
    public List<string> setProductIDs;

    // 備考
    public string notes;
}

public enum DistributionType
{
    Normal, // 通常頒布
    Free,   // 無料配布
    Limited // 数量限定
}
