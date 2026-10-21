using Microsoft.AspNetCore.Mvc;
using MotorShop.AdminPanel.Models;
using MotorShop.AdminPanel.Services;
using Smartstore.Core.Common.Configuration;
using Smartstore.Core.Security;
using Smartstore.Web.Controllers;

namespace MotorShop.AdminPanel.Controllers;

[Route("motor-panel/orders")]
public class MotorPanelOrderController : AdminController
{
    private readonly OrderPanelService _orders;
    private readonly PdfSettings _pdfSettings;

    public MotorPanelOrderController(OrderPanelService orders, PdfSettings pdfSettings)
    {
        _orders = orders;
        _pdfSettings = pdfSettings;
    }

    [HttpGet("")]
    [Permission(Permissions.Order.Read)]
    public async Task<IActionResult> Index(OrderFilter filter)
    {
        PanelHttp.NoIndex(Response);
        return View(await _orders.GetOrdersAsync(filter ?? new()));
    }

    [HttpGet("list")]
    [Permission(Permissions.Order.Read)]
    public async Task<IActionResult> List(OrderFilter filter)
    {
        PanelHttp.NoIndex(Response);
        return PartialView("_OrderGrid", await _orders.GetOrdersAsync(filter ?? new()));
    }

    [HttpGet("{id:int}")]
    [Permission(Permissions.Order.Read)]
    public async Task<IActionResult> Details(int id)
    {
        PanelHttp.NoIndex(Response);

        var model = await _orders.GetOrderAsync(id);
        if (model == null)
        {
            return NotFound();
        }

        model.CanUpdate = await Services.Permissions.AuthorizeAsync(Permissions.Order.Update);
        model.CanEditShipments = await Services.Permissions.AuthorizeAsync(Permissions.Order.EditShipment);
        model.PdfUrl = _pdfSettings.Enabled ? Url.Action("Print", "Order", new { id, pdf = true, area = "" }) : null;

        return PartialView("_OrderDetails", model);
    }

    [HttpPost("{id:int}/note")]
    [Permission(Permissions.Order.Update)]
    public async Task<IActionResult> Note(int id, string note, bool displayToCustomer)
        => Result(await _orders.AddNoteAsync(id, note, displayToCustomer), "Order.NoteAdded");

    [HttpPost("{id:int}/mark-paid")]
    [Permission(Permissions.Order.Update)]
    public async Task<IActionResult> MarkPaid(int id)
        => Result(await _orders.MarkPaidAsync(id), "Order.MarkedPaid");

    [HttpPost("{id:int}/cancel")]
    [Permission(Permissions.Order.Update)]
    public async Task<IActionResult> Cancel(int id, bool notifyCustomer)
        => Result(await _orders.CancelAsync(id, notifyCustomer), "Order.Cancelled");

    [HttpPost("{id:int}/complete")]
    [Permission(Permissions.Order.Update)]
    public async Task<IActionResult> Complete(int id)
        => Result(await _orders.CompleteAsync(id), "Order.Completed");

    [HttpGet("{id:int}/shipment")]
    [Permission(Permissions.Order.EditShipment)]
    public async Task<IActionResult> NewShipment(int id)
    {
        PanelHttp.NoIndex(Response);

        var model = await _orders.GetShipmentFormAsync(id);
        if (model == null)
        {
            return NotFound();
        }

        return PartialView("_ShipmentForm", model);
    }

    [HttpPost("{id:int}/shipment")]
    [Permission(Permissions.Order.EditShipment)]
    public async Task<IActionResult> NewShipment(int id, string carrier, string trackingNumber)
    {
        // Quantities arrive as qty_{orderItemId}.
        var quantities = new Dictionary<int, int>();
        foreach (var (key, value) in Request.Form)
        {
            if (key.StartsWith("qty_", StringComparison.Ordinal)
                && int.TryParse(key[4..], out var itemId)
                && int.TryParse(value, out var qty)
                && qty > 0)
            {
                quantities[itemId] = qty;
            }
        }

        var error = await _orders.AddShipmentAsync(id, carrier, trackingNumber, quantities);
        if (error != null)
        {
            Response.StatusCode = 422;
            return Json(new { success = false, errors = new Dictionary<string, string> { [""] = error } });
        }

        return Json(new { success = true, message = T("Plugins.MotorShop.AdminPanel.Shipment.Created").Value });
    }

    [HttpPost("shipment/{shipmentId:int}/ship")]
    [Permission(Permissions.Order.EditShipment)]
    public async Task<IActionResult> Ship(int shipmentId, bool notifyCustomer)
        => Result(await _orders.ShipAsync(shipmentId, notifyCustomer), "Shipment.Shipped");

    [HttpPost("shipment/{shipmentId:int}/deliver")]
    [Permission(Permissions.Order.EditShipment)]
    public async Task<IActionResult> Deliver(int shipmentId, bool notifyCustomer)
        => Result(await _orders.DeliverAsync(shipmentId, notifyCustomer), "Shipment.Delivered");

    private IActionResult Result(string error, string successKey)
    {
        if (error != null)
        {
            Response.StatusCode = 422;
            return Json(new { success = false, error });
        }

        return Json(new { success = true, message = T("Plugins.MotorShop.AdminPanel." + successKey).Value });
    }
}

[Route("motor-panel/shipments")]
public class MotorPanelShipmentController : AdminController
{
    private readonly OrderPanelService _orders;

    public MotorPanelShipmentController(OrderPanelService orders)
    {
        _orders = orders;
    }

    [HttpGet("")]
    [Permission(Permissions.Order.Read)]
    public async Task<IActionResult> Index()
    {
        PanelHttp.NoIndex(Response);

        ViewBag.CanEditShipments = await Services.Permissions.AuthorizeAsync(Permissions.Order.EditShipment);
        return View(await _orders.GetShipmentQueueAsync());
    }
}

[Route("motor-panel/customers")]
public class MotorPanelCustomerController : AdminController
{
    private readonly OrderPanelService _orders;

    public MotorPanelCustomerController(OrderPanelService orders)
    {
        _orders = orders;
    }

    [HttpGet("")]
    [Permission(Permissions.Customer.Read)]
    public async Task<IActionResult> Index(CustomerFilter filter)
    {
        PanelHttp.NoIndex(Response);
        return View(await _orders.GetCustomersAsync(filter ?? new()));
    }

    [HttpGet("list")]
    [Permission(Permissions.Customer.Read)]
    public async Task<IActionResult> List(CustomerFilter filter)
    {
        PanelHttp.NoIndex(Response);
        return PartialView("_CustomerGrid", await _orders.GetCustomersAsync(filter ?? new()));
    }

    [HttpGet("{id:int}")]
    [Permission(Permissions.Customer.Read)]
    public async Task<IActionResult> Details(int id)
    {
        PanelHttp.NoIndex(Response);

        var model = await _orders.GetCustomerAsync(id);
        if (model == null)
        {
            return NotFound();
        }

        ViewBag.CanReadOrders = await Services.Permissions.AuthorizeAsync(Permissions.Order.Read);
        return PartialView("_CustomerDetails", model);
    }
}

/// <summary>
/// Invoice status. No certified e-invoice integrator is configured, so this screen only shows that state
/// and links the internal order summary (which is not a legal invoice). See docs/motor/invoicing.md.
/// </summary>
[Route("motor-panel/invoices")]
public class MotorPanelInvoiceController : AdminController
{
    private readonly OrderPanelService _orders;
    private readonly PdfSettings _pdfSettings;

    public MotorPanelInvoiceController(OrderPanelService orders, PdfSettings pdfSettings)
    {
        _orders = orders;
        _pdfSettings = pdfSettings;
    }

    [HttpGet("")]
    [Permission(Permissions.Order.Read)]
    public async Task<IActionResult> Index(OrderFilter filter)
    {
        PanelHttp.NoIndex(Response);

        filter ??= new();
        return View(new InvoiceListModel
        {
            Orders = await _orders.GetOrdersAsync(filter),
            PdfEnabled = _pdfSettings.Enabled
        });
    }
}
