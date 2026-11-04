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
    public int setDiscountQuantity;
    public int setDiscountPrice;
    public string setDiscountGroup;
    public int bonusThresholdAmount;
    public int bonusQuantity;
    public int endingStock;
    public int undoStockTarget;
}

[Serializable]
public class RegisteredEventDefinition
{
    public string registeredEventID;
    public string eventName;
    public List<string> productIDs = new List<string>();
    public bool isArchived;
}

[Serializable]
public class RegisterChangeCash
{
    public int denomination;
    public int count;
}

[Serializable]
public class RegisterSaleLine
{
    public string productID;
    public string productName;
    public int unitPrice;
    public int quantity;
    public bool isBonus;
    public long lineTotal;
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
    public string registeredEventID;
    public bool isLocked;
    public bool deletionPending;
    public bool isPractice;
    public bool hasChangeFloatSnapshot;
    public List<RegisterChangeCash> startingChangeCash = new List<RegisterChangeCash>();
    public List<RegisterChangeCash> remainingChangeCash = new List<RegisterChangeCash>();
    public List<RegisterProductSnapshot> products = new List<RegisterProductSnapshot>();
    public List<RegisterSale> sales = new List<RegisterSale>();
}

[Serializable]
public class OfflineRegisterDocument
{
    public int schemaVersion = 2;
    public RegisterEventRecord activeEvent;
    public List<RegisterEventRecord> completedEvents = new List<RegisterEventRecord>();
    public List<RegisteredEventDefinition> registeredEvents = new List<RegisteredEventDefinition>();
    public List<RegisterChangeCash> configuredChangeCash = new List<RegisterChangeCash>();
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
    private static readonly int[] ChangeDenominations = { 10000, 5000, 1000, 500, 100, 50, 10, 5, 1 };

    private string SavePath => Path.Combine(Application.persistentDataPath, "register-data.json");
    private OfflineRegisterDocument document = new OfflineRegisterDocument();

    public RegisterEventRecord ActiveEvent => document.activeEvent;
    public IReadOnlyList<RegisterEventRecord> CompletedEvents => document.completedEvents;
    public IReadOnlyList<RegisteredEventDefinition> RegisteredEvents => document.registeredEvents;
    public IReadOnlyList<RegisterChangeCash> ConfiguredChangeCash => document.configuredChangeCash;
    public bool IsFinalizing => document.activeEvent != null && document.activeEvent.status == FinalizingStatus;
    public string LoadError { get; private set; }

    private void Awake()
    {
        Load();
        if(document != null && document.activeEvent != null && document.activeEvent.isPractice)
        {
            RegisterEventRecord abandonedPractice = document.activeEvent;
            document.activeEvent = null;
            if(!Save(out string errorMessage))
            {
                document.activeEvent = abandonedPractice;
                Debug.LogWarning("おためしレジを終了できませんでした：" + errorMessage);
            }
        }
    }

    public bool StartEvent(string eventName, ProductCatalog catalog, out string errorMessage)
    {
        return StartEvent(eventName, null, catalog, null, false, out errorMessage);
    }

    public bool StartEvent(RegisteredEventDefinition definition, ProductCatalog catalog, out string errorMessage)
    {
        if(definition == null)
        {
            errorMessage = "開始する登録イベントを選択してください。";
            return false;
        }

        return StartEvent(definition.eventName, definition.registeredEventID, catalog, definition.productIDs, false, out errorMessage);
    }

    public bool StartPracticeEvent(ProductCatalog catalog, out string errorMessage)
    {
        return StartEvent("おためしレジ", null, catalog, null, true, out errorMessage);
    }

    public bool SetConfiguredChangeCash(List<RegisterChangeCash> counts, out string errorMessage)
    {
        errorMessage = null;
        if(!CanWrite(out errorMessage))
        {
            return false;
        }

        if(document.activeEvent != null)
        {
            errorMessage = "イベント中は釣銭の準備数を変更できません。";
            return false;
        }

        if(!TryNormalizeChangeCash(counts, out List<RegisterChangeCash> normalized, out errorMessage))
        {
            return false;
        }

        List<RegisterChangeCash> previous = CloneChangeCash(document.configuredChangeCash);
        document.configuredChangeCash = normalized;
        if(Save(out errorMessage))
        {
            return true;
        }

        document.configuredChangeCash = previous;
        return false;
    }

    public bool DeleteRegisteredEvent(string registeredEventID, out string errorMessage)
    {
        errorMessage = null;
        if(!CanWrite(out errorMessage))
        {
            return false;
        }

        RegisteredEventDefinition definition = document.registeredEvents.Find(item => item != null && item.registeredEventID == registeredEventID);
        if(definition == null || definition.isArchived)
        {
            errorMessage = "削除できる登録イベントが見つかりません。履歴のあるイベントは削除できません。";
            return false;
        }

        if(document.activeEvent != null && document.activeEvent.registeredEventID == registeredEventID)
        {
            errorMessage = "会計中のイベントは削除できません。";
            return false;
        }

        int index = document.registeredEvents.IndexOf(definition);
        document.registeredEvents.RemoveAt(index);
        if(Save(out errorMessage))
        {
            return true;
        }

        document.registeredEvents.Insert(index, definition);
        return false;
    }

    public long GetConfiguredChangeAmount()
    {
        return GetChangeCashAmount(document.configuredChangeCash);
    }

    public long GetStartingChangeAmount(RegisterEventRecord record)
    {
        return GetChangeCashAmount(record == null ? null : record.startingChangeCash);
    }

    public long GetRemainingChangeAmount(RegisterEventRecord record)
    {
        return GetChangeCashAmount(record == null ? null : record.remainingChangeCash);
    }

    public IReadOnlyList<RegisterChangeCash> GetChangeCashCounts(RegisterEventRecord record)
    {
        return record == null ? document.configuredChangeCash : record.remainingChangeCash;
    }

    public bool SaveRegisteredEvent(RegisteredEventDefinition definition, out string errorMessage)
    {
        errorMessage = null;
        if(!CanWrite(out errorMessage))
        {
            return false;
        }

        if(definition == null || string.IsNullOrWhiteSpace(definition.eventName))
        {
            errorMessage = "イベント名を入力してください。";
            return false;
        }

        if(definition.productIDs == null || definition.productIDs.Count == 0)
        {
            errorMessage = "このイベントで販売する商品を1点以上選択してください。";
            return false;
        }

        if(document.activeEvent != null && document.activeEvent.registeredEventID == definition.registeredEventID)
        {
            errorMessage = "会計中のイベント登録は編集できません。";
            return false;
        }

        RegisteredEventDefinition existing = string.IsNullOrEmpty(definition.registeredEventID)
            ? null
            : document.registeredEvents.Find(item => item != null && item.registeredEventID == definition.registeredEventID);
        if(existing != null && existing.isArchived)
        {
            errorMessage = "アーカイブ済みの登録イベントは編集できません。";
            return false;
        }

        string oldName = existing == null ? null : existing.eventName;
        List<string> oldProducts = existing == null ? null : new List<string>(existing.productIDs);
        if(existing == null)
        {
            definition.registeredEventID = Guid.NewGuid().ToString("N");
            definition.eventName = definition.eventName.Trim();
            definition.isArchived = false;
            definition.productIDs = new List<string>(definition.productIDs);
            document.registeredEvents.Add(definition);
        }
        else
        {
            existing.eventName = definition.eventName.Trim();
            existing.productIDs = new List<string>(definition.productIDs);
        }

        if(Save(out errorMessage))
        {
            return true;
        }

        if(existing == null)
        {
            document.registeredEvents.Remove(definition);
        }
        else
        {
            existing.eventName = oldName;
            existing.productIDs = oldProducts;
        }

        return false;
    }

    public bool SetCompletedEventLocked(string eventID, bool isLocked, out string errorMessage)
    {
        errorMessage = null;
        if(!CanWrite(out errorMessage))
        {
            return false;
        }

        RegisterEventRecord record = document.completedEvents.Find(item => item != null && item.eventID == eventID);
        if(record == null || record.deletionPending)
        {
            errorMessage = "変更できる売上履歴が見つかりません。";
            return false;
        }

        bool oldValue = record.isLocked;
        record.isLocked = isLocked;
        if(Save(out errorMessage))
        {
            return true;
        }

        record.isLocked = oldValue;
        return false;
    }

    public bool DeleteCompletedEvent(string eventID, ProductCatalog catalog, out string errorMessage)
    {
        errorMessage = null;
        if(!CanWrite(out errorMessage))
        {
            return false;
        }

        RegisterEventRecord record = document.completedEvents.Find(item => item != null && item.eventID == eventID);
        if(record == null || catalog == null || catalog.products == null)
        {
            errorMessage = "削除する売上履歴または商品カタログが見つかりません。";
            return false;
        }

        if(record.isLocked && !record.deletionPending)
        {
            errorMessage = "ロック中の履歴は削除できません。先にロックを解除してください。";
            return false;
        }

        if(!record.deletionPending)
        {
            foreach(RegisterProductSnapshot snapshot in record.products)
            {
                ProductData product = catalog.products.Find(item => item != null && item.productID == snapshot.productID);
                if(product == null)
                {
                    errorMessage = $"在庫を戻せません。商品が見つかりません：{snapshot.productName}";
                    return false;
                }

                long target = (long)product.initialStock + GetProductSold(record, snapshot.productID);
                if(target > int.MaxValue)
                {
                    errorMessage = $"在庫数が上限を超えるため削除できません：{snapshot.productName}";
                    return false;
                }

                snapshot.undoStockTarget = (int)target;
            }

            record.deletionPending = true;
            if(!Save(out errorMessage))
            {
                record.deletionPending = false;
                return false;
            }
        }

        return FinishPendingDelete(record, catalog, out errorMessage);
    }

    private bool FinishPendingDelete(RegisterEventRecord record, ProductCatalog catalog, out string errorMessage)
    {
        errorMessage = null;
        foreach(RegisterProductSnapshot snapshot in record.products)
        {
            ProductData product = catalog.products.Find(item => item != null && item.productID == snapshot.productID);
            if(product == null)
            {
                errorMessage = $"在庫を戻せません。商品が見つかりません：{snapshot.productName}";
                return false;
            }

            product.initialStock = snapshot.undoStockTarget;
        }

        try
        {
            catalog.SaveProducts();
        }
        catch(Exception exception)
        {
            errorMessage = "在庫を戻せませんでした。再度削除を実行してください：" + exception.Message;
            return false;
        }

        document.completedEvents.Remove(record);
        RegisteredEventDefinition definition = document.registeredEvents.Find(item => item != null && item.registeredEventID == record.registeredEventID);
        bool wasArchived = definition != null && definition.isArchived;
        if(definition != null)
        {
            definition.isArchived = false;
        }

        if(Save(out errorMessage))
        {
            return true;
        }

        document.completedEvents.Insert(0, record);
        if(definition != null)
        {
            definition.isArchived = wasArchived;
        }
        return false;
    }

    private bool StartEvent(string eventName, string registeredEventID, ProductCatalog catalog, List<string> productIDsToSell, bool isPractice, out string errorMessage)
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
            registeredEventID = registeredEventID,
            startedAt = DateTime.Now.ToString("o"),
            status = ActiveStatus,
            isPractice = isPractice,
            hasChangeFloatSnapshot = true,
            startingChangeCash = CloneChangeCash(document.configuredChangeCash),
            remainingChangeCash = CloneChangeCash(document.configuredChangeCash)
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

            if(productIDsToSell != null && !productIDsToSell.Contains(product.productID))
            {
                continue;
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
                unitPrice = ResolveUnitPrice(product),
                setDiscountQuantity = Mathf.Max(0, product.setDiscountQuantity),
                setDiscountPrice = Mathf.Max(0, product.setDiscountPrice),
                setDiscountGroup = (product.setDiscountGroup ?? string.Empty).Trim(),
                bonusThresholdAmount = Mathf.Max(0, product.bonusThresholdAmount),
                bonusQuantity = Mathf.Max(0, product.bonusQuantity)
            });
        }

        if(record.products.Count == 0)
        {
            errorMessage = "販売できる商品がありません。商品を登録し直してください。";
            return false;
        }

        if(productIDsToSell != null &&
            (record.products.Count != new HashSet<string>(productIDsToSell).Count || new HashSet<string>(productIDsToSell).Count != productIDsToSell.Count))
        {
            errorMessage = "登録イベントの商品設定に、削除済みまたは重複した商品があります。登録イベントを編集してください。";
            return false;
        }

        if(registeredEventID != null)
        {
            RegisteredEventDefinition savedDefinition = document.registeredEvents.Find(item => item != null && item.registeredEventID == registeredEventID);
            if(savedDefinition == null || savedDefinition.isArchived)
            {
                errorMessage = "選択した登録イベントは開始できません。もう一度一覧を確認してください。";
                return false;
            }
        }

        var discountGroups = new Dictionary<string, RegisterProductSnapshot>(StringComparer.OrdinalIgnoreCase);
        foreach(RegisterProductSnapshot snapshot in record.products)
        {
            if(snapshot == null || string.IsNullOrWhiteSpace(snapshot.setDiscountGroup))
            {
                continue;
            }

            string group = snapshot.setDiscountGroup.Trim();
            if(discountGroups.TryGetValue(group, out RegisterProductSnapshot first))
            {
                bool hasDiscount = first.setDiscountQuantity > 0 || snapshot.setDiscountQuantity > 0 || first.setDiscountPrice > 0 || snapshot.setDiscountPrice > 0;
                if(hasDiscount && (first.unitPrice != snapshot.unitPrice || first.setDiscountQuantity != snapshot.setDiscountQuantity || first.setDiscountPrice != snapshot.setDiscountPrice))
                {
                    errorMessage = $"セット割引グループ「{group}」の商品は、通常価格・セット点数・セット合計を揃えてください。";
                    return false;
                }
            }
            else
            {
                discountGroups.Add(group, snapshot);
            }
        }

        foreach(KeyValuePair<string, RegisterProductSnapshot> group in discountGroups)
        {
            RegisterProductSnapshot snapshot = group.Value;
            if(snapshot.setDiscountQuantity > 0 && (snapshot.setDiscountQuantity < 2 || snapshot.setDiscountPrice <= 0 || snapshot.setDiscountPrice >= (long)snapshot.unitPrice * snapshot.setDiscountQuantity))
            {
                errorMessage = $"セット割引グループ「{group.Key}」の割引設定が正しくありません。商品情報を確認してください。";
                return false;
            }
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

        long paidSubtotal = 0;
        var quantities = new Dictionary<string, int>();
        foreach(CartItem item in cart)
        {
            if(item == null || item.quantity <= 0)
            {
                errorMessage = "カートの数量が正しくありません。";
                return false;
            }

            RegisterProductSnapshot snapshot = record.products.Find(product => product != null && product.productID == item.productID);
            if(snapshot == null)
            {
                errorMessage = $"イベントに登録されていない商品です：{item.productName}";
                return false;
            }

            quantities[item.productID] = quantities.TryGetValue(item.productID, out int oldQuantity)
                ? oldQuantity + item.quantity
                : item.quantity;
            if(!item.isBonus)
            {
                long rawLineTotal = (long)snapshot.unitPrice * item.quantity;
                if(rawLineTotal < 0 || paidSubtotal > long.MaxValue - rawLineTotal)
                {
                    errorMessage = "合計金額が大きすぎます。";
                    return false;
                }

                paidSubtotal += rawLineTotal;
            }
        }

        foreach(KeyValuePair<string, int> entry in quantities)
        {
            if(entry.Value > GetRemainingStock(entry.Key))
            {
                errorMessage = "在庫が足りません。商品一覧を確認してください。";
                return false;
            }
        }

        foreach(RegisterProductSnapshot snapshot in record.products)
        {
            if(snapshot == null || snapshot.bonusThresholdAmount <= 0 || snapshot.bonusQuantity <= 0)
            {
                continue;
            }

            int paidQuantity = cart.Find(item => item != null && !item.isBonus && item.productID == snapshot.productID)?.quantity ?? 0;
            int expectedBonus = paidSubtotal >= snapshot.bonusThresholdAmount &&
                GetRemainingStock(snapshot.productID) >= paidQuantity + snapshot.bonusQuantity
                ? snapshot.bonusQuantity
                : 0;
            int actualBonus = 0;
            foreach(CartItem item in cart)
            {
                if(item != null && item.isBonus && item.productID == snapshot.productID)
                {
                    actualBonus += item.quantity;
                }
            }

            if(actualBonus != expectedBonus)
            {
                errorMessage = "おまけの条件を再確認してください。商品一覧へ戻ってカートを更新してください。";
                return false;
            }
        }

        Dictionary<CartItem, long> calculatedLines = CalculateCartLineTotals(record, cart);
        long total = 0;
        var sale = new RegisterSale
        {
            transactionID = Guid.NewGuid().ToString("N"),
            completedAt = DateTime.Now.ToString("o"),
            received = received
        };

        foreach(CartItem cartItem in cart)
        {
            RegisterProductSnapshot snapshot = record.products == null
                ? null
                : record.products.Find(item => item != null && item.productID == cartItem.productID);
            if(snapshot == null)
            {
                errorMessage = $"イベントに登録されていない商品です：{cartItem.productName}";
                return false;
            }

            long lineTotal = calculatedLines[cartItem];
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
                unitPrice = cartItem.isBonus ? 0 : snapshot.unitPrice,
                quantity = cartItem.quantity,
                isBonus = cartItem.isBonus,
                lineTotal = lineTotal
            });
        }

        if(received < total)
        {
            errorMessage = "受け取り金額が合計金額に足りません。";
            return false;
        }

        sale.total = total;
        sale.change = received - total;
        List<RegisterChangeCash> previousChangeCash = CloneChangeCash(record.remainingChangeCash);
        if(!TryPayChange(record.remainingChangeCash, sale.change, out List<RegisterChangeCash> updatedChangeCash))
        {
            errorMessage = $"おつり {sale.change:N0}円を準備できません。お釣り管理で硬貨・紙幣の残数を確認してください。";
            return false;
        }

        record.remainingChangeCash = updatedChangeCash;
        record.sales.Add(sale);

        if(!Save(out errorMessage))
        {
            record.sales.Remove(sale);
            record.remainingChangeCash = previousChangeCash;
            return false;
        }

        change = sale.change;
        return true;
    }

    public long CalculateCartTotal(List<CartItem> cart)
    {
        long total = 0;
        if(cart == null || document.activeEvent == null || document.activeEvent.products == null)
        {
            return total;
        }

        Dictionary<CartItem, long> lineTotals = CalculateCartLineTotals(document.activeEvent, cart);
        foreach(KeyValuePair<CartItem, long> line in lineTotals)
        {
            if(line.Value <= 0)
            {
                continue;
            }

            long lineTotal = line.Value;
            if(lineTotal > 0 && total <= long.MaxValue - lineTotal)
            {
                total += lineTotal;
            }
        }

        return total;
    }

    public long CalculateCartItemTotal(string productID, int quantity, bool isBonus = false)
    {
        RegisterEventRecord record = document.activeEvent;
        RegisterProductSnapshot snapshot = record == null || record.products == null
            ? null
            : record.products.Find(product => product != null && product.productID == productID);
        return CalculateLineTotal(snapshot, quantity, isBonus);
    }

    public bool IsGroupSetDiscountActive(List<CartItem> cart, string productID)
    {
        RegisterEventRecord record = document.activeEvent;
        RegisterProductSnapshot selected = record == null || record.products == null
            ? null
            : record.products.Find(product => product != null && product.productID == productID);
        if(selected == null || string.IsNullOrWhiteSpace(selected.setDiscountGroup) || selected.setDiscountQuantity < 2 || cart == null)
        {
            return false;
        }

        int totalQuantity = 0;
        foreach(CartItem item in cart)
        {
            if(item == null || item.isBonus)
            {
                continue;
            }

            RegisterProductSnapshot snapshot = record.products.Find(product => product != null && product.productID == item.productID);
            if(snapshot != null && string.Equals(snapshot.setDiscountGroup, selected.setDiscountGroup, StringComparison.OrdinalIgnoreCase))
            {
                totalQuantity += item.quantity;
            }
        }

        return totalQuantity >= selected.setDiscountQuantity;
    }

    private static Dictionary<CartItem, long> CalculateCartLineTotals(RegisterEventRecord record, List<CartItem> cart)
    {
        var result = new Dictionary<CartItem, long>();
        var groupItems = new Dictionary<string, List<CartItem>>(StringComparer.OrdinalIgnoreCase);
        if(record == null || record.products == null || cart == null)
        {
            return result;
        }

        foreach(CartItem item in cart)
        {
            if(item == null)
            {
                continue;
            }

            RegisterProductSnapshot snapshot = record.products.Find(product => product != null && product.productID == item.productID);
            if(item.isBonus)
            {
                result[item] = 0;
            }
            else if(snapshot != null && !string.IsNullOrWhiteSpace(snapshot.setDiscountGroup) && snapshot.setDiscountQuantity >= 2)
            {
                string group = snapshot.setDiscountGroup.Trim();
                if(!groupItems.TryGetValue(group, out List<CartItem> items))
                {
                    items = new List<CartItem>();
                    groupItems.Add(group, items);
                }
                items.Add(item);
            }
            else
            {
                result[item] = CalculateLineTotal(snapshot, item.quantity, false);
            }
        }

        foreach(KeyValuePair<string, List<CartItem>> group in groupItems)
        {
            RegisterProductSnapshot groupSettings = record.products.Find(product => product != null &&
                string.Equals(product.setDiscountGroup, group.Key, StringComparison.OrdinalIgnoreCase));
            if(groupSettings == null || groupSettings.setDiscountQuantity < 2)
            {
                foreach(CartItem item in group.Value)
                {
                    RegisterProductSnapshot snapshot = record.products.Find(product => product != null && product.productID == item.productID);
                    result[item] = snapshot == null ? 0 : (long)snapshot.unitPrice * item.quantity;
                }
                continue;
            }

            int groupQuantity = 0;
            foreach(CartItem item in group.Value)
            {
                groupQuantity += item.quantity;
            }

            int discountQuantity = groupQuantity / groupSettings.setDiscountQuantity * groupSettings.setDiscountQuantity;
            int discountUnitsAssigned = 0;
            foreach(CartItem item in group.Value)
            {
                RegisterProductSnapshot snapshot = record.products.Find(product => product != null && product.productID == item.productID);
                int discountedUnits = Math.Min(item.quantity, discountQuantity - discountUnitsAssigned);
                if(discountedUnits < 0)
                {
                    discountedUnits = 0;
                }

                long discountedCost = GetDiscountedPrefixCost(discountUnitsAssigned + discountedUnits, groupSettings) -
                    GetDiscountedPrefixCost(discountUnitsAssigned, groupSettings);
                long regularCost = (long)Math.Max(0, item.quantity - discountedUnits) * (snapshot == null ? 0 : snapshot.unitPrice);
                result[item] = discountedCost + regularCost;
                discountUnitsAssigned += discountedUnits;
            }
        }

        return result;
    }

    private static long GetDiscountedPrefixCost(int quantity, RegisterProductSnapshot settings)
    {
        int setSize = settings.setDiscountQuantity;
        long completeSets = quantity / setSize;
        int remainder = quantity % setSize;
        long perItemBase = settings.setDiscountPrice / setSize;
        long remainderYen = settings.setDiscountPrice % setSize;
        return completeSets * settings.setDiscountPrice + remainder * perItemBase + Math.Min(remainder, remainderYen);
    }

    private static long CalculateLineTotal(RegisterProductSnapshot snapshot, int quantity, bool isBonus)
    {
        if(snapshot == null || quantity <= 0 || isBonus)
        {
            return 0;
        }

        if(string.IsNullOrWhiteSpace(snapshot.setDiscountGroup) && snapshot.setDiscountQuantity >= 2 && snapshot.setDiscountPrice >= 0)
        {
            int sets = quantity / snapshot.setDiscountQuantity;
            int remainder = quantity % snapshot.setDiscountQuantity;
            long setTotal = (long)sets * snapshot.setDiscountPrice;
            long remainderTotal = (long)remainder * snapshot.unitPrice;
            if(setTotal <= long.MaxValue - remainderTotal)
            {
                return setTotal + remainderTotal;
            }
        }

        return (long)snapshot.unitPrice * quantity;
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

        if(record.isPractice)
        {
            document.activeEvent = null;
            if(Save(out errorMessage))
            {
                return true;
            }

            document.activeEvent = record;
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
            snapshot.endingStock = product.initialStock;
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
        RegisteredEventDefinition definition = document.registeredEvents.Find(item => item != null && item.registeredEventID == record.registeredEventID);
        if(definition != null)
        {
            definition.isArchived = true;
        }

        if(!Save(out errorMessage))
        {
            document.completedEvents.Remove(record);
            document.activeEvent = record;
            record.status = FinalizingStatus;
            if(definition != null)
            {
                definition.isArchived = false;
            }
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

    public long GetEventChangeTotal(RegisterEventRecord record)
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
                total += sale.change;
            }
        }

        return total;
    }

    public long GetProductSold(RegisterEventRecord record, string productID)
    {
        if(record == null || record.sales == null)
        {
            return 0;
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
                if(line != null && line.productID == productID)
                {
                    sold += line.quantity;
                }
            }
        }

        return sold;
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

        foreach(RegisterEventRecord record in new List<RegisterEventRecord>(document.completedEvents))
        {
            if(record != null && record.deletionPending && !FinishPendingDelete(record, catalog, out string errorMessage))
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
            document.configuredChangeCash = CreateDefaultChangeCash();
            return;
        }

        try
        {
            document = JsonUtility.FromJson<OfflineRegisterDocument>(File.ReadAllText(SavePath));
            if(document == null || document.completedEvents == null)
            {
                throw new InvalidDataException("レジデータの形式が正しくありません。");
            }

            if(document.registeredEvents == null)
            {
                document.registeredEvents = new List<RegisteredEventDefinition>();
            }

            NormalizeConfiguredChangeCash();

            foreach(RegisteredEventDefinition definition in document.registeredEvents)
            {
                if(definition != null && definition.productIDs == null)
                {
                    definition.productIDs = new List<string>();
                }
            }

            if(document.activeEvent != null)
            {
                if(document.activeEvent.startingChangeCash == null || document.activeEvent.startingChangeCash.Count == 0)
                {
                    document.activeEvent.startingChangeCash = CloneChangeCash(document.configuredChangeCash);
                    document.activeEvent.remainingChangeCash = CloneChangeCash(document.configuredChangeCash);
                    document.activeEvent.hasChangeFloatSnapshot = false;
                }
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
                        NormalizeConfiguredChangeCash();
                        if(document.registeredEvents == null)
                        {
                            document.registeredEvents = new List<RegisteredEventDefinition>();
                        }
                        foreach(RegisteredEventDefinition definition in document.registeredEvents)
                        {
                            if(definition != null && definition.productIDs == null)
                            {
                                definition.productIDs = new List<string>();
                            }
                        }
                        if(document.activeEvent != null)
                        {
                            if(document.activeEvent.startingChangeCash == null || document.activeEvent.startingChangeCash.Count == 0)
                            {
                                document.activeEvent.startingChangeCash = CloneChangeCash(document.configuredChangeCash);
                                document.activeEvent.remainingChangeCash = CloneChangeCash(document.configuredChangeCash);
                                document.activeEvent.hasChangeFloatSnapshot = false;
                            }
                            NormalizeEvent(document.activeEvent);
                        }
                        foreach(RegisterEventRecord completed in document.completedEvents)
                        {
                            if(completed != null)
                            {
                                NormalizeEvent(completed);
                            }
                        }

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
            document.configuredChangeCash = CreateDefaultChangeCash();
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

    private static List<RegisterChangeCash> CreateDefaultChangeCash()
    {
        return new List<RegisterChangeCash>
        {
            new RegisterChangeCash { denomination = 10000, count = 0 },
            new RegisterChangeCash { denomination = 5000, count = 1 },
            new RegisterChangeCash { denomination = 1000, count = 5 },
            new RegisterChangeCash { denomination = 500, count = 4 },
            new RegisterChangeCash { denomination = 100, count = 10 },
            new RegisterChangeCash { denomination = 50, count = 0 },
            new RegisterChangeCash { denomination = 10, count = 0 },
            new RegisterChangeCash { denomination = 5, count = 0 },
            new RegisterChangeCash { denomination = 1, count = 0 }
        };
    }

    private void NormalizeConfiguredChangeCash()
    {
        if(document.configuredChangeCash == null || document.configuredChangeCash.Count == 0)
        {
            document.configuredChangeCash = CreateDefaultChangeCash();
            return;
        }

        if(!TryNormalizeChangeCash(document.configuredChangeCash, out List<RegisterChangeCash> normalized, out string errorMessage))
        {
            document.configuredChangeCash = CreateDefaultChangeCash();
            Debug.LogWarning("釣銭設定を初期値に戻しました：" + errorMessage);
            return;
        }

        document.configuredChangeCash = normalized;
    }

    private static List<RegisterChangeCash> CreateEmptyChangeCash()
    {
        var result = new List<RegisterChangeCash>();
        foreach(int denomination in ChangeDenominations)
        {
            result.Add(new RegisterChangeCash { denomination = denomination, count = 0 });
        }

        return result;
    }

    private static List<RegisterChangeCash> CloneChangeCash(List<RegisterChangeCash> source)
    {
        var result = new List<RegisterChangeCash>();
        if(source != null)
        {
            foreach(RegisterChangeCash item in source)
            {
                if(item != null)
                {
                    result.Add(new RegisterChangeCash { denomination = item.denomination, count = item.count });
                }
            }
        }

        return result;
    }

    private static bool TryNormalizeChangeCash(List<RegisterChangeCash> source, out List<RegisterChangeCash> normalized, out string errorMessage)
    {
        normalized = CreateEmptyChangeCash();
        errorMessage = null;
        if(source == null)
        {
            errorMessage = "釣銭の内訳を読み込めませんでした。";
            return false;
        }

        foreach(RegisterChangeCash item in source)
        {
            if(item == null || Array.IndexOf(ChangeDenominations, item.denomination) < 0 || item.count < 0 || item.count > 999)
            {
                errorMessage = "釣銭の枚数は、各種類0〜999枚で設定してください。";
                return false;
            }

            if(source.FindAll(candidate => candidate != null && candidate.denomination == item.denomination).Count != 1)
            {
                errorMessage = "釣銭の種類が重複しています。";
                return false;
            }

            RegisterChangeCash target = normalized.Find(candidate => candidate.denomination == item.denomination);
            target.count = item.count;
        }

        return true;
    }

    private static long GetChangeCashAmount(List<RegisterChangeCash> counts)
    {
        long amount = 0;
        if(counts != null)
        {
            foreach(RegisterChangeCash item in counts)
            {
                if(item != null && item.denomination > 0 && item.count > 0)
                {
                    amount += (long)item.denomination * item.count;
                }
            }
        }

        return amount;
    }

    private static bool TryPayChange(List<RegisterChangeCash> source, long amount, out List<RegisterChangeCash> updated)
    {
        updated = CloneChangeCash(source);
        if(amount < 0)
        {
            return false;
        }

        long remaining = amount;
        foreach(int denomination in ChangeDenominations)
        {
            RegisterChangeCash item = updated.Find(candidate => candidate.denomination == denomination);
            if(item == null || item.count <= 0)
            {
                continue;
            }

            int used = (int)Math.Min(item.count, remaining / denomination);
            item.count -= used;
            remaining -= (long)used * denomination;
        }

        return remaining == 0;
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

        if(record.startingChangeCash == null || record.startingChangeCash.Count == 0)
        {
            record.startingChangeCash = CreateEmptyChangeCash();
            record.hasChangeFloatSnapshot = false;
        }

        if(record.remainingChangeCash == null || record.remainingChangeCash.Count == 0)
        {
            record.remainingChangeCash = CloneChangeCash(record.startingChangeCash);
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

