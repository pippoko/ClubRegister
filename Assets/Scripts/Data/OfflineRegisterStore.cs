using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable]
public class RegisterProductSnapshot
{
    public string productID;
    public string productName;
    public int startingStock;
    public int unitPrice;
}

[Serializable]
public class RegisterSaleLine
{
    public string productID;
    public string productName;
    public int unitPrice;
    public int quantity;
}

[Serializable]
public class RegisterSale
{
    public string transactionID;
    public string completedAt;
    public List<RegisterSaleLine> lines = new List<RegisterSaleLine>();
    public long total;
    public long received;
    public long change;
}

[Serializable]
public class RegisterEventRecord
{
    public string eventID;
    public string eventName;
    public string startedAt;
    public string completedAt;
    public string status;
    public List<RegisterProductSnapshot> products = new List<RegisterProductSnapshot>();
    public List<RegisterSale> sales = new List<RegisterSale>();
}

[Serializable]
public class OfflineRegisterDocument
{
    public int schemaVersion = 1;
    public RegisterEventRecord activeEvent;
    public List<RegisterEventRecord> completedEvents = new List<RegisterEventRecord>();
}

/// <summary>
/// Saves the current event and completed sales locally so register operations work offline.
/// Inventory is only written to ProductCatalog when an event is finalized.
/// </summary>
public class OfflineRegisterStore : MonoBehaviour
{
    private const string ActiveStatus = "active";
    private const string FinalizingStatus = "finalizing";
    private const string CompletedStatus = "completed";

    private string SavePath => Path.Combine(Application.persistentDataPath, "register-data.json");
    private OfflineRegisterDocument document = new OfflineRegisterDocument();

    public RegisterEventRecord ActiveEvent => document.activeEvent;
    public IReadOnlyList<RegisterEventRecord> CompletedEvents => document.completedEvents;
    public bool IsFinalizing => document.activeEvent != null && document.activeEvent.status == FinalizingStatus;
    public string LoadError { get; private set; }

    private void Awake()
    {
        Load();
    }

    public bool StartEvent(string eventName, ProductCatalog catalog, out string errorMessage)
    {
        errorMessage = null;

        if(!CanWrite(out errorMessage))
        {
            return false;
        }

        if(document.activeEvent != null)
        {
            errorMessage = "イベントがすでに開催中です。";
            return false;
        }

        if(catalog == null || catalog.products == null || catalog.products.Count == 0)
        {
            errorMessage = "先に商品を登録してください。";
            return false;
        }

        var record = new RegisterEventRecord
        {
            eventID = Guid.NewGuid().ToString("N"),
            eventName = string.IsNullOrWhiteSpace(eventName) ? "イベント" : eventName.Trim(),
            startedAt = DateTime.Now.ToString("o"),
            status = ActiveStatus
        };

        var productIDs = new HashSet<string>();
        bool assignedProductIDs = false;
        foreach(ProductData product in catalog.products)
        {
            if(product == null)
            {
                continue;
            }

            if(string.IsNullOrWhiteSpace(product.productID))
            {
                product.productID = Guid.NewGuid().ToString("N");
                assignedProductIDs = true;
            }

            if(!productIDs.Add(product.productID))
            {
                errorMessage = $"商品IDが重複しています：{product.productName}";
                return false;
            }

            record.products.Add(new RegisterProductSnapshot
            {
                productID = product.productID,
                productName = product.productName ?? "名称未設定",
                startingStock = Mathf.Max(0, product.initialStock),
                unitPrice = ResolveUnitPrice(product)
            });
        }

        if(record.products.Count == 0)
        {
            errorMessage = "販売できる商品がありません。商品を登録し直してください。";
            return false;
        }

        if(assignedProductIDs)
        {
            try
            {
                catalog.SaveProducts();
            }
            catch(Exception exception)
            {
                errorMessage = $"商品の識別情報を保存できませんでした：{exception.Message}";
                return false;
            }
        }

        document.activeEvent = record;
        if(!Save(out errorMessage))
        {
            document.activeEvent = null;
            return false;
        }

        return true;
    }

    public int GetRemainingStock(string productID)
    {
        RegisterEventRecord record = document.activeEvent;
        if(record == null || record.products == null)
        {
            return 0;
        }

        RegisterProductSnapshot snapshot = record.products.Find(item => item != null && item.productID == productID);
        if(snapshot == null)
        {
            return 0;
        }

        long sold = 0;
        if(record.sales != null)
        {
            foreach(RegisterSale sale in record.sales)
            {
                if(sale == null || sale.lines == null)
                {
                    continue;
                }

                foreach(RegisterSaleLine line in sale.lines)
                {
                    if(line != null && line.productID == productID)
                    {
                        sold += line.quantity;
                    }
                }
            }
        }

        return (int)Math.Max(0L, snapshot.startingStock - sold);
    }

    public bool RecordSale(List<CartItem> cart, long received, out long change, out string errorMessage)
    {
        change = 0;
        errorMessage = null;

        if(!CanWrite(out errorMessage))
        {
            return false;
        }

        RegisterEventRecord record = document.activeEvent;
        if(record == null || record.status != ActiveStatus)
        {
            errorMessage = "開催中のイベントがありません。";
            return false;
        }

        if(cart == null || cart.Count == 0)
        {
            errorMessage = "商品が選択されていません。";
            return false;
        }

        long total = 0;
        var sale = new RegisterSale
        {
            transactionID = Guid.NewGuid().ToString("N"),
            completedAt = DateTime.Now.ToString("o"),
            received = received
        };

        foreach(CartItem cartItem in cart)
        {
            if(cartItem == null || cartItem.quantity <= 0)
            {
                errorMessage = "カートの数量が正しくありません。";
                return false;
            }

            RegisterProductSnapshot snapshot = record.products == null
                ? null
                : record.products.Find(item => item != null && item.productID == cartItem.productID);
            if(snapshot == null)
            {
                errorMessage = $"イベントに登録されていない商品です：{cartItem.productName}";
                return false;
            }

            if(cartItem.quantity > GetRemainingStock(cartItem.productID))
            {
                errorMessage = $"在庫が足りません：{snapshot.productName}";
                return false;
            }

            long lineTotal = (long)snapshot.unitPrice * cartItem.quantity;
            if(lineTotal < 0 || total > long.MaxValue - lineTotal)
            {
                errorMessage = "合計金額が大きすぎます。";
                return false;
            }

            total += lineTotal;
            sale.lines.Add(new RegisterSaleLine
            {
                productID = snapshot.productID,
                productName = snapshot.productName,
                unitPrice = snapshot.unitPrice,
                quantity = cartItem.quantity
            });
        }

        if(received < total)
        {
            errorMessage = "受け取り金額が合計金額に足りません。";
            return false;
        }

        sale.total = total;
        sale.change = received - total;
        record.sales.Add(sale);

        if(!Save(out errorMessage))
        {
            record.sales.Remove(sale);
            return false;
        }

        change = sale.change;
        return true;
    }

    public bool CompleteEvent(ProductCatalog catalog, out string errorMessage)
    {
        errorMessage = null;

        if(!CanWrite(out errorMessage))
        {
            return false;
        }

        RegisterEventRecord record = document.activeEvent;
        if(record == null)
        {
            errorMessage = "開催中のイベントがありません。";
            return false;
        }

        if(catalog == null || catalog.products == null)
        {
            errorMessage = "商品カタログが見つかりません。";
            return false;
        }

        // Save a recovery marker first. If the app closes during finalization,
        // absolute stock values make retrying safe and prevent double subtraction.
        record.status = FinalizingStatus;
        if(!Save(out errorMessage))
        {
            record.status = ActiveStatus;
            return false;
        }

        if(record.products == null || record.sales == null)
        {
            errorMessage = "イベントデータが不完全なため、在庫を確定できません。保存データを保持しています。";
            return false;
        }

        foreach(RegisterProductSnapshot snapshot in record.products)
        {
            if(snapshot == null)
            {
                errorMessage = "商品データが不完全なため、在庫を確定できません。";
                return false;
            }

            ProductData product = catalog.products.Find(item => item != null && item.productID == snapshot.productID);
            if(product == null)
            {
                errorMessage = $"在庫を確定できません。商品が見つかりません：{snapshot.productName}";
                return false;
            }

            long sold = 0;
            foreach(RegisterSale sale in record.sales)
            {
                if(sale == null || sale.lines == null)
                {
                    continue;
                }

                foreach(RegisterSaleLine line in sale.lines)
                {
                    if(line != null && line.productID == snapshot.productID)
                    {
                        sold += line.quantity;
                    }
                }
            }

            product.initialStock = (int)Math.Max(0L, snapshot.startingStock - sold);
        }

        try
        {
            catalog.SaveProducts();
        }
        catch(Exception exception)
        {
            errorMessage = $"在庫を保存できませんでした。再度終了を実行してください：{exception.Message}";
            return false;
        }

        record.status = CompletedStatus;
        record.completedAt = DateTime.Now.ToString("o");
        document.completedEvents.Insert(0, record);
        document.activeEvent = null;

        if(!Save(out errorMessage))
        {
            document.completedEvents.Remove(record);
            document.activeEvent = record;
            record.status = FinalizingStatus;
            return false;
        }

        return true;
    }

    public long GetEventSalesTotal(RegisterEventRecord record)
    {
        if(record == null || record.sales == null)
        {
            return 0;
        }

        long total = 0;
        foreach(RegisterSale sale in record.sales)
        {
            if(sale != null)
            {
                total += sale.total;
            }
        }

        return total;
    }

    public long GetEventItemCount(RegisterEventRecord record)
    {
        if(record == null || record.sales == null)
        {
            return 0;
        }

        long count = 0;
        foreach(RegisterSale sale in record.sales)
        {
            if(sale == null || sale.lines == null)
            {
                continue;
            }

            foreach(RegisterSaleLine line in sale.lines)
            {
                if(line != null)
                {
                    count += line.quantity;
                }
            }
        }

        return count;
    }

    public void RetryFinalization(ProductCatalog catalog)
    {
        if(document.activeEvent != null && document.activeEvent.status == FinalizingStatus)
        {
            if(!CompleteEvent(catalog, out string errorMessage))
            {
                Debug.LogError(errorMessage);
            }
        }
    }

    private void Load()
    {
        string backupPath = SavePath + ".bak";

        if(!File.Exists(SavePath) && File.Exists(backupPath))
        {
            File.Copy(backupPath, SavePath, true);
        }

        if(!File.Exists(SavePath))
        {
            document = new OfflineRegisterDocument();
            return;
        }

        try
        {
            document = JsonUtility.FromJson<OfflineRegisterDocument>(File.ReadAllText(SavePath));
            if(document == null || document.completedEvents == null)
            {
                throw new InvalidDataException("レジデータの形式が正しくありません。");
            }

            if(document.activeEvent != null)
            {
                NormalizeEvent(document.activeEvent);
            }

            foreach(RegisterEventRecord completed in document.completedEvents)
            {
                if(completed != null)
                {
                    NormalizeEvent(completed);
                }
            }
        }
        catch(Exception exception)
        {
            try
            {
                if(File.Exists(backupPath))
                {
                    document = JsonUtility.FromJson<OfflineRegisterDocument>(File.ReadAllText(backupPath));
                    if(document != null && document.completedEvents != null)
                    {
                        File.Copy(backupPath, SavePath, true);
                        Debug.LogWarning("レジデータを読み込めなかったため、バックアップから復元しました。");
                        return;
                    }
                }
            }
            catch(Exception backupException)
            {
                LoadError = $"レジデータを読み込めませんでした：{backupException.Message}";
            }

            if(string.IsNullOrEmpty(LoadError))
            {
                LoadError = $"レジデータを読み込めませんでした：{exception.Message}";
            }

            document = new OfflineRegisterDocument();
            Debug.LogError(LoadError);
        }
    }

    private bool Save(out string errorMessage)
    {
        errorMessage = null;

        if(!CanWrite(out errorMessage))
        {
            return false;
        }

        string temporaryPath = SavePath + ".tmp";
        string backupPath = SavePath + ".bak";

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SavePath));
            File.WriteAllText(temporaryPath, JsonUtility.ToJson(document, true));

            if(File.Exists(SavePath))
            {
                try
                {
                    File.Replace(temporaryPath, SavePath, backupPath);
                }
                catch(PlatformNotSupportedException)
                {
                    ReplaceWithBackup(temporaryPath, backupPath);
                }
                catch(IOException)
                {
                    ReplaceWithBackup(temporaryPath, backupPath);
                }
            }
            else
            {
                File.Move(temporaryPath, SavePath);
            }

            return true;
        }
        catch(Exception exception)
        {
            errorMessage = $"レジデータを保存できませんでした：{exception.Message}";
            return false;
        }
    }

    private void ReplaceWithBackup(string temporaryPath, string backupPath)
    {
        File.Copy(SavePath, backupPath, true);
        File.Delete(SavePath);
        File.Move(temporaryPath, SavePath);
    }

    private bool CanWrite(out string errorMessage)
    {
        errorMessage = LoadError;
        return string.IsNullOrEmpty(LoadError);
    }

    private static int ResolveUnitPrice(ProductData product)
    {
        if(product.distributionType == DistributionType.Free)
        {
            return 0;
        }

        return product.ActivePrice > 0 ? product.ActivePrice : Mathf.Max(0, product.DefaultPrice);
    }

    private static void NormalizeEvent(RegisterEventRecord record)
    {
        if(record.products == null)
        {
            record.products = new List<RegisterProductSnapshot>();
        }

        if(record.sales == null)
        {
            record.sales = new List<RegisterSale>();
        }

        foreach(RegisterSale sale in record.sales)
        {
            if(sale != null && sale.lines == null)
            {
                sale.lines = new List<RegisterSaleLine>();
            }
        }
    }
}

