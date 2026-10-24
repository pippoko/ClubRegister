using System.Text;
using TMPro;
using UnityEngine;

public class ProductListView : MonoBehaviour
{
    [SerializeField] private TMP_Text productListText;

    private void Start()
    {
        ProductCatalog catalog = FindFirstObjectByType<ProductCatalog>();

        if(catalog == null)
        {
            productListText.text = "商品カタログが見つかりません。";
            Debug.LogError("シーンにProductCatalogがありません。");
            return;
        }

        var text = new StringBuilder();

        foreach(ProductData product in catalog.products)
        {
            text.AppendLine($"{product.productName}　{product.DefaultPrice}円");
        }

        productListText.text = text.Length > 0
            ? text.ToString()
            : "商品が登録されていません。";
    }
}