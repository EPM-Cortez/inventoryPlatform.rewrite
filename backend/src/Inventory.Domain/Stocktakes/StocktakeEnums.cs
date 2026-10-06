namespace Inventory.Domain.Stocktakes;

public enum StocktakeScope
{
    All,
    Location,
    Category,
}

public enum StocktakeStatus
{
    InProgress,
    Completed,
    Cancelled,
}
