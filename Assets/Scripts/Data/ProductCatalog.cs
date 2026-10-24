using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable]
public class ProductCatalogSaveData
{
    public List<ProductData> products;
}

public class ProductCatalog : MonoBehaviour
{
    public List<ProductData> products = new List<ProductData>();

    private string SavePath => 
        Path.Combine(Application.persistentDataPath, "products.json");

    private void Awake()
    {
        if(File.Exists(SavePath))
        {
            LoadProducts();
        }
        else
        {
            Debug.Log($"保存ファイルがないため、Inspectorの商品データを使います。登録商品数：{products.Count}");
        }
    }

    [ContextMenu("Save Products to JSON")]
    public void SaveProducts()
    {
        var saveData = new ProductCatalogSaveData
        {
            products = products
        };

        string json = JsonUtility.ToJson(saveData, true);
        File.WriteAllText(SavePath, json);

        Debug.Log($"商品を保存しました：{SavePath}");
    }

    [ContextMenu("Load Products from JSON")]
    public void LoadProducts()
    {
        if(!File.Exists(SavePath))
        {
            Debug.LogWarning($"保存ファイルがありません：{SavePath}");
            return;
        }

        string json = File.ReadAllText(SavePath);
        var saveData = JsonUtility.FromJson<ProductCatalogSaveData>(json);

        products = saveData?.products ?? new List<ProductData>();

        Debug.Log($"商品を読み込みました。登録商品数：{products.Count}");
    }
}