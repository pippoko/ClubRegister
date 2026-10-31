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
        if(products == null)
        {
            products = new List<ProductData>();
        }

        if(File.Exists(SavePath))
        {
            LoadProducts();
        }
        else
        {
            Debug.Log($"保存ファイルがないため、Inspectorの商品データを使います。登録商品数：{products.Count}");
        }
    }

    public bool TryAddProduct(ProductData product, out string errorMessage)
    {
        errorMessage = null;

        if(product == null)
        {
            errorMessage = "商品情報がありません。";
            return false;
        }

        if(string.IsNullOrWhiteSpace(product.productName))
        {
            errorMessage = "商品名を入力してください。";
            return false;
        }

        if(product.DefaultPrice < 0 || product.initialStock < 0)
        {
            errorMessage = "価格と在庫数は0以上で入力してください。";
            return false;
        }

        if(products == null)
        {
            products = new List<ProductData>();
        }

        if(string.IsNullOrWhiteSpace(product.productID))
        {
            product.productID = Guid.NewGuid().ToString("N");
        }

        if(products.Exists(item => item != null && item.productID == product.productID))
        {
            errorMessage = "同じ商品IDがすでに登録されています。";
            return false;
        }

        if(product.variationProductIDs == null)
        {
            product.variationProductIDs = new List<string>();
        }

        if(product.setProductIDs == null)
        {
            product.setProductIDs = new List<string>();
        }

        products.Add(product);

        try
        {
            SaveProducts();
            return true;
        }
        catch(Exception exception)
        {
            products.Remove(product);
            errorMessage = $"商品を保存できませんでした：{exception.Message}";
            return false;
        }
    }

    public bool TryUpdateProduct(
        string productID,
        string productName,
        int price,
        int stock,
        string imagePath,
        int setDiscountQuantity,
        int setDiscountPrice,
        string setDiscountGroup,
        int bonusThresholdAmount,
        int bonusQuantity,
        out string errorMessage)
    {
        errorMessage = null;

        if(string.IsNullOrWhiteSpace(productName))
        {
            errorMessage = "商品名を入力してください。";
            return false;
        }

        if(price < 0 || stock < 0)
        {
            errorMessage = "価格と在庫数は0以上で入力してください。";
            return false;
        }

        ProductData product = products?.Find(item => item != null && item.productID == productID);
        if(product == null)
        {
            errorMessage = "編集する商品が見つかりません。";
            return false;
        }

        string oldName = product.productName;
        int oldPrice = product.DefaultPrice;
        int oldActivePrice = product.ActivePrice;
        int oldStock = product.initialStock;
        string oldImagePath = product.imagePath;
        int oldSetDiscountQuantity = product.setDiscountQuantity;
        int oldSetDiscountPrice = product.setDiscountPrice;
        string oldSetDiscountGroup = product.setDiscountGroup;
        int oldBonusThresholdAmount = product.bonusThresholdAmount;
        int oldBonusQuantity = product.bonusQuantity;

        product.productName = productName.Trim();
        product.DefaultPrice = price;
        product.ActivePrice = price;
        product.initialStock = stock;
        product.imagePath = imagePath?.Trim() ?? string.Empty;
        product.setDiscountQuantity = Mathf.Max(0, setDiscountQuantity);
        product.setDiscountPrice = Mathf.Max(0, setDiscountPrice);
        product.setDiscountGroup = setDiscountGroup?.Trim() ?? string.Empty;
        product.bonusThresholdAmount = Mathf.Max(0, bonusThresholdAmount);
        product.bonusQuantity = Mathf.Max(0, bonusQuantity);

        try
        {
            SaveProducts();
            return true;
        }
        catch(Exception exception)
        {
            product.productName = oldName;
            product.DefaultPrice = oldPrice;
            product.ActivePrice = oldActivePrice;
            product.initialStock = oldStock;
            product.imagePath = oldImagePath;
            product.setDiscountQuantity = oldSetDiscountQuantity;
            product.setDiscountPrice = oldSetDiscountPrice;
            product.setDiscountGroup = oldSetDiscountGroup;
            product.bonusThresholdAmount = oldBonusThresholdAmount;
            product.bonusQuantity = oldBonusQuantity;
            errorMessage = $"商品を保存できませんでした：{exception.Message}";
            return false;
        }
    }

    [ContextMenu("Save Products to JSON")]
    public void SaveProducts()
    {
        if(products == null)
        {
            products = new List<ProductData>();
        }

        var saveData = new ProductCatalogSaveData
        {
            products = products
        };

        string json = JsonUtility.ToJson(saveData, true);
        string temporaryPath = SavePath + ".tmp";
        string backupPath = SavePath + ".bak";

        Directory.CreateDirectory(Path.GetDirectoryName(SavePath));
        File.WriteAllText(temporaryPath, json);

        if(File.Exists(SavePath))
        {
            try
            {
                File.Replace(temporaryPath, SavePath, backupPath);
            }
            catch(PlatformNotSupportedException)
            {
                File.Copy(SavePath, backupPath, true);
                File.Delete(SavePath);
                File.Move(temporaryPath, SavePath);
            }
            catch(IOException)
            {
                File.Copy(SavePath, backupPath, true);
                File.Delete(SavePath);
                File.Move(temporaryPath, SavePath);
            }
        }
        else
        {
            File.Move(temporaryPath, SavePath);
        }

        Debug.Log($"商品を保存しました：{SavePath}");
    }

    [ContextMenu("Load Products from JSON")]
    public void LoadProducts()
    {
        string backupPath = SavePath + ".bak";
        if(!File.Exists(SavePath) && File.Exists(backupPath))
        {
            File.Copy(backupPath, SavePath, true);
        }

        if(!File.Exists(SavePath))
        {
            Debug.LogWarning($"保存ファイルがありません：{SavePath}");
            return;
        }

        try
        {
            string json = File.ReadAllText(SavePath);
            var saveData = JsonUtility.FromJson<ProductCatalogSaveData>(json);

            if(saveData == null || saveData.products == null)
            {
                throw new InvalidDataException("商品データの形式が正しくありません。");
            }

            products = saveData.products;
            Debug.Log($"商品を読み込みました。登録商品数：{products.Count}");
        }
        catch(Exception exception)
        {
            if(File.Exists(backupPath))
            {
                try
                {
                    string backupJson = File.ReadAllText(backupPath);
                    var backupData = JsonUtility.FromJson<ProductCatalogSaveData>(backupJson);
                    if(backupData != null && backupData.products != null)
                    {
                        products = backupData.products;
                        Debug.LogWarning("保存中の商品ファイルを読み込めなかったため、バックアップから復元しました。");
                        return;
                    }
                }
                catch(Exception backupException)
                {
                    Debug.LogError($"商品データとバックアップを読み込めませんでした：{backupException.Message}");
                }
            }

            Debug.LogError($"商品データを読み込めませんでした。既存ファイルは保持しています：{exception.Message}");
        }
    }
}

