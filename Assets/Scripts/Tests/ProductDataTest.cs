using UnityEngine;

public class ProductDataTest : MonoBehaviour
{
    private void Start()
    {
        var product = new ProductData
        {
            productID = "book_001",
            productName = "新刊イラスト集",
            description = "イベント頒布用のサンプル商品",

            DefaultPrice = 500,
            publishedDate = "2026-10-16",

            orderdQuantity = 100,
            initialStock = 100,
            reorderThreshold = 10,

            imagePath = "",

            distributionType = DistributionType.Normal,
            isR18 = false,
            isSetOnly = false,
            isOneOfAKind = false,

            variationProductIDs = new
                System.Collections.Generic.List<string>(),
                setProductIDs = new System.Collections.Generic.List<string>(),

            notes = ""
        };

        Debug.Log($"商品名：{product.productName}");
        Debug.Log($"価格：{product.DefaultPrice}円");
        Debug.Log($"商品ID：{product.productID}");
        Debug.Log($"頒布区分：{product.distributionType}");
    }
}