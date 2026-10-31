using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class CartItem
{
    public string productID;
    public string productName;
    public int unitPrice;
    public int quantity;
    public bool isBonus;
}

public class CartManager : MonoBehaviour
{
    [SerializeField] private List<CartItem> items = new List<CartItem>();

    public int TotalPrice
    {
        get
        {
            int total = 0;

            foreach(CartItem item in items)
            {
                total += item.unitPrice * item.quantity;
            }

            return total;
        }
    }

    public void AddProduct(ProductData product)
    {
        if(product == null)
        {
            Debug.LogWarning("カートに追加する商品がありません。");
            return;
        }

        CartItem existingItem = items.Find(
            item => item.productID == product.productID
        );

        if(existingItem != null)
        {
            existingItem.quantity++;
        }
        else
        {
            items.Add(new CartItem
            {
                productID = product.productID,
                productName = product.productName,
                unitPrice = product.DefaultPrice,
                quantity = 1
            });
        }
    }

    [ContextMenu("Test: Add First Product Twice")]
    private void TestAddFirstProductTwice()
    {
        ProductCatalog catalog = FindFirstObjectByType<ProductCatalog>();

        if(catalog == null || catalog.products == null || catalog.products.Count == 0)
        {
            Debug.LogWarning("商品カタログに商品がありません。");
            return;
        }

        AddProduct(catalog.products[0]);

        Debug.Log(
            $"テスト追加：{catalog.products[0].productName} / " +
            $"数量：{items[0].quantity} / 合計：{TotalPrice}円"
        );
    }
}
