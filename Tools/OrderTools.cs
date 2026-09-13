using System.ComponentModel;

namespace customersupport.Tools
{
    public static class OrderTools
    {
        [Description("Looks up order details by order ID,Returns order info including items,total,and shipping address")]
        public static string LookupOrder([Description("The order ID,for example ORD-1234")] string orderId)
        {
            var orders = new Dictionary<string, string>
            {
                ["ORD-1001"] = "Order  ORD-1001:  Wireless headphones ($79.99),shipped to 123 Main St.Status : Delivered",
                ["ORD-1002"] = "Order  ORD-1002:  Smart Speaker ($129.99)+ USB-C Cable($12.99),shipped to 456 Oak Ave.Status : In Transit",
                ["ORD-1003"] = "Order  ORD-1003:  Laptop ($49.99),shipped to 789 Pine Rd.Status : Processing."
            };

            return orders.TryGetValue(orderId, out var orderDetails) ? orderDetails : $"No Order found with ID {orderId}.please check the order ID and try again.";
        }

        [Description("Gets the current shipping status of an order")]
        public static string GetOrderStatus([Description("The order ID to check status for")] string orderId)
        {
            var statuses = new Dictionary<string, string>
            {
                ["ORD-1001"] = "Delivered on March 5,2026.",
                ["ORD-1002"] = "In Transit - Expected delivery March 11,2026.",
                ["ORD-1003"] = "Processing - not yet shipped"
            };
            return statuses.TryGetValue(orderId, out var status) ? $"Order {orderId} : {status}" : $"No Order found with ID {orderId}.please check the order ID and try again.";
        }

        [Description("cancels an order.Only works for orders that have not shipped yet.Important:always confirm with the customer before calling this tool")]
        public static string CancelOrder([Description("The order ID to cancel")] string orderId, [Description("Must be 'true' - the customer must explicitly confirm the cancallaction")] string customerConfirmed)
        {
            if (!customerConfirmed.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                return "Cancellation not confirmed. Please ask the customer to confirm they want to cancel the order.";
            }

            var cancellableOrders = new HashSet<string> { "ORD-1003" };
            if (cancellableOrders.Contains(orderId))
            {
                return $"Order {orderId} has been successfully canceled.a confirmation email will been sent shortly";
            }
            return $"Order {orderId} cannot be canceled - it has already shipped or been delivered. Please check the order ID and try again."; 
        }
    }
}
