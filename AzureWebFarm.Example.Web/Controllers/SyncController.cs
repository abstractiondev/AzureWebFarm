using System;
using System.Linq;
using System.Web.Mvc;
using AzureWebFarm.Example.Web.Models;
using AzureWebFarm.Services;
using AzureWebFarm.Storage;

namespace AzureWebFarm.Example.Web.Controllers
{
    [Authorize]
    public class SyncController : Controller
    {
        private readonly SyncStatusRepository _syncStatusRepository;

        public SyncController()
            : this(new SyncStatusRepository())
        {
        }

        public SyncController(SyncStatusRepository syncStatusRepository)
        {
            _syncStatusRepository = syncStatusRepository;
        }

        public ActionResult Index(string webSiteName)
        {
            if (string.IsNullOrWhiteSpace(webSiteName))
            {
                throw new ArgumentNullException("webSiteName");
            }

            var webSiteStatus = _syncStatusRepository.RetrieveSyncStatus(webSiteName);
            var model = webSiteStatus
                .Where(s => s.IsOnline)
                .Select(s => new SyncStatusModel
                {
                    RoleInstanceId = s.RoleInstanceId,
                    Status = s.Status.ToString(),
                    SyncTimestamp = s.SyncTimestamp
                }
            );

            ViewBag.WebSiteName = webSiteName;

            return View(model);
        }

        public ActionResult SyncChange(bool enable)
        {
            if (enable)
            {
                SyncService.SyncEnable();
            }
            else
            {
                SyncService.SyncDisable();
            }
            return RedirectToAction("Index", "WebSite");
        }
    }
}