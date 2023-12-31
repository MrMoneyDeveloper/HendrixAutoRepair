﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using System.Net;
using System.Web;
using System.Web.Mvc;
using BookStore.Models;
using System.Net.Mail;
using BookStore.ViewModels;
using BookStore.Helpers;
using System.EnterpriseServices;
using System.Web.Helpers;
using Microsoft.AspNetCore.Http;
using Org.BouncyCastle.Asn1.Ocsp;
using System.Net.Http;
using System.Text;
using System.Configuration;
using System.Text.Json;

namespace BookStore.Controllers
{
    public class SalesController : Controller
    {
        private ApplicationDbContext db = new ApplicationDbContext();
        private readonly IHttpContextAccessor _httpContextAccessor;

        public SalesController()
        {

        }
        public SalesController(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }
        // GET: 
        //[Authorize(Roles = "Driver")]
        public ViewResult Index(string sortOrder, string searchString, ApplicationUser applicationUser, Sale sale)
        {


            ViewBag.NameSortParm = String.IsNullOrEmpty(sortOrder) ? "name_desc" : "";
            ViewBag.DateSortParm = sortOrder == "Date" ? "date_desc" : "Date";
            var students = from s in db.Sales
                           select s;
            if (!String.IsNullOrEmpty(searchString))
            {
                students = students.Where(s => s.Name.Contains(searchString)
                                       || s.ApplicationUser.UserName.ToUpper().Contains(searchString.ToUpper()));
            }
            switch (sortOrder)
            {
                case "name_desc":
                    students = students.OrderByDescending(s => s.Name);
                    break;
                case "Date":
                    students = students.OrderBy(s => s.SaleDate);
                    break;
                case "date_desc":
                    students = students.OrderByDescending(s => s.SaleDate);
                    break;
                default:
                    students = students.OrderBy(s => s.SaleDate);
                    break;
            }


            return View(students.ToList());
        }

        public async Task<ActionResult> Delivered(int id)
        {
            int DelId = 0;
            Sale sale = await db.Sales.FindAsync(id);

            var deliverys = from db in db.Deliveries
                            where db.SaleId == id
                            select db;

            foreach (var item in deliverys)
            {
                DelId = item.DeliveryId;
            }

            Delivery delivery = await db.Deliveries.FindAsync(DelId);

            delivery.DeliveryDate = DateTime.Now.ToString();
            delivery.CurrentLocation = sale.Address;
            delivery.isDelivered = true;


            string subject = "Successful Delivery For Order Number #" + sale.SaleId;
            string body = "Dear Customer " + sale.Name + ", your order has been delivered successfully.";
            try
            {
                new Email().SendEmail(subject, body, sale.Email);
            }
            catch (Exception)
            {

                // throw;
            }

            sale.Complete = true;
            sale.OrderStatus = "Order Delivered To Customer";
            sale.ConfirmDelivery = true;
            db.Entry(sale).State = EntityState.Modified;
            db.SaveChanges();

            //Set Sale State to Complete once delivered
            var completedSaleId = delivery.SaleId;
            if (Convert.ToString(completedSaleId) != null || completedSaleId > 0)
            {
                Sale completedSale = db.Sales.Find(completedSaleId);
                completedSale.Complete = true;
            }
            db.Entry(delivery).State = EntityState.Modified;
            await db.SaveChangesAsync();
            return RedirectToAction("Details" + "/" + id);
        }

        public async Task<ActionResult> ApprovedDispatch(int aid)
        {

            Sale sale = await db.Sales.FindAsync(aid);

            var deliverys = from db in db.Deliveries
                            where db.SaleId == aid
                            select db;

            // CO = sale.ConfirmOrder;
            // CO = true;
            //  ViewBag.ConfirmOrder = CO;

            var url = Request.Url;

            //Successful Delivery...
            string subject = "Approved Delivery For Order Number #" + sale.SaleId;
            string body = "Dear Customer" + sale.Name + " your order has been Dispatched. " + string.Format("{0}://{1}/Manage/PurchaseHistory Use this link to confirm you have received your order", url.Scheme, url.Authority) /* https://2022grp32.azurewebsites.net/Manage/PurchaseHistory */;
            try
            {
                new Email().SendEmail(subject, body, sale.Email);
            }
            catch (Exception)
            {

                // throw;
            }

            sale.ConfirmOrder = false;
            sale.Dispatched = true;
            sale.ConfirmDelivery = null;
            sale.OrderStatus = "In Awaiting Order Confirmation";

            await db.SaveChangesAsync();
            return RedirectToAction("Details" + "/" + aid);
        }

        public async Task<ActionResult> ConfirmOrder(int hiv)
        {
            Sale sale = await db.Sales.FindAsync(hiv);

            var deliverys = from db in db.Deliveries
                            where db.SaleId == hiv
                            select db;

            if (sale.ConfirmOrder == false)
            {
                sale.ConfirmOrder = true;
                sale.ConfirmDelivery = false;
                sale.OrderStatus = "Order Confirmed By Customer";
            }
            else
            { ViewBag.ConfirmOrder = "Sceduled"; }

            // sale.ConfirmOrder = false;
            // sale.Dispatched = true;
            await db.SaveChangesAsync();
            return RedirectToAction("Details" + "/" + hiv);
        }
        // GET: Sales/Details/5

        public async Task<ActionResult> Details([Bind(Include = "ConfirmOrder,Dispatched,ConfirmDelivery")] int? id, string message)
        {

            ViewBag.message = !string.IsNullOrEmpty(message) ? message : "";

            int DelId = 0;
            int SaleId = 0;
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            Sale sale = await db.Sales.FindAsync(id);


            if (sale == null)
            {
                return HttpNotFound();
            }



            var productList = from db in db.SaleDetails
                              where db.SaleId == id
                              select db.Products;

            var deliverys = from db in db.Deliveries
                            where db.SaleId == id
                            select db;

            var confirmation = from db in db.Sales
                               where db.SaleId == id
                               select db;



            ViewBag.SaleDetails = productList;
            ViewBag.DeliveryDetails = confirmation;

            foreach (var item in deliverys)
            {
                DelId = item.DeliveryId;
            }

            Delivery delivery = await db.Deliveries.OrderByDescending(a => a.DeliveryId).FirstOrDefaultAsync(c => c.DeliveryId == DelId);
            sale.ConfirmDelivery = delivery.isDelivered;
            ViewBag.Date = delivery.DeliveryDate;
            ViewBag.Delivery = delivery.CurrentLocation;
            ViewBag.SalesId = id;


            return View(sale);
        }

        public async Task<ActionResult> SendCustomerOtp(Int32 Id)
        {
            Delivery delivery = await db.Deliveries.OrderByDescending(a => a.DeliveryId).FirstOrDefaultAsync(c => c.SaleId == Id);
            delivery.CustomerOTP = new Random().Next(100000, 1000000);
            db.Entry(delivery).State = EntityState.Modified;
            //await db.SaveChangesAsync();

            Sale sale = await db.Sales.FindAsync(Id);
            ApplicationUser user = await db.Users.FirstOrDefaultAsync(a => a.UserName == sale.Email);

            SmsNotification smsNotification = new SmsNotification();
            SMSViewModel sms = new SMSViewModel();
            sms.messages = new Message[]
            {
                new Message
            {
                to =String.Format("+27{0}", user.PhoneNo.Substring(Math.Max(0, user.PhoneNo.Length - 9))),
                source = "php",
                body = $"{delivery.CustomerOTP} is your Hendrix auto delivery confirmation code.",
                custom_string = $"{delivery.CustomerOTP} is your Hendrix auto delivery confirmation code."
            }
            };


            string username = ConfigurationManager.AppSettings["sms_username"];
            string api_key = ConfigurationManager.AppSettings["sms_api_key"];

            // Concatenate username and password with a colon
            string credentials = String.Format("{0}:{1}", username, api_key);

            // Convert the concatenated string to a byte array
            byte[] credentialsBytes = Encoding.UTF8.GetBytes(credentials);

            // Encode the byte array as a Base64 string
            string base64Credentials = Convert.ToBase64String(credentialsBytes);

            var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Post, "https://rest.clicksend.com/v3/sms/send");
            request.Headers.Add("Authorization", $"Basic {base64Credentials}");
            var content = new StringContent(JsonSerializer.Serialize(sms), null, "application/json");
            request.Content = content;
            var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            //Console.WriteLine(await response.Content.ReadAsStringAsync());
            smsNotification.Request = JsonSerializer.Serialize(sms);
            smsNotification.Response = await response.Content.ReadAsStringAsync();
            smsNotification.SaleId = sale.SaleId;
            smsNotification.DeliveryId = delivery.DeliveryId;
            smsNotification.CreatedDateTime = DateTime.Now;
            smsNotification.Driver = User.Identity.Name;
            db.SmsNotifications.Add(smsNotification);
            await db.SaveChangesAsync();
            return Json(true, JsonRequestBehavior.AllowGet);
        }

        public async Task<ActionResult> ValidateCustomersOTP(Int32 Id, Int32 OTP)
        {
            Delivery delivery = await db.Deliveries.OrderByDescending(a => a.DeliveryId).FirstOrDefaultAsync(c => c.SaleId == Id && c.CustomerOTP == OTP);
            if (delivery == null) return Json(false, JsonRequestBehavior.AllowGet);
            Sale sale = await db.Sales.FindAsync(Id);

            var deliverys = from db in db.Deliveries
                            where db.SaleId == Id
                            select db;

            if (sale.ConfirmOrder == false)
            {
                sale.ConfirmOrder = true;
                sale.ConfirmDelivery = false;
                sale.OrderStatus = "Order Confirmed By Customer";
            }
            await db.SaveChangesAsync();
            return Json(true, JsonRequestBehavior.AllowGet);
        }

        public async Task<ActionResult> PickUpOrder(string message)
        {
            var FindAllDeliver = await db.Deliveries.Include(x => x.sale).Where(x => x.IsForReturn == true).ToListAsync();
            ViewBag.message = message;
            return View(FindAllDeliver);
        }

        public class SMSViewModel
        {
            public Message[] messages { get; set; }
        }

        public class Message
        {
            public string source { get; set; }
            public string body { get; set; }
            public string to { get; set; }
            public string custom_string { get; set; }
        }


        public ActionResult PickUpCom(int? id)
        {
            string message = message = string.Format($" Delivery Pick Up is Failed to update  at {DateTime.Now.ToString("dd-MMMM-yyyy")}");
            using (ApplicationDbContext core = new ApplicationDbContext())
            {
                try
                {
                    var FindDelivery = core.Deliveries.FirstOrDefault(x => x.DeliveryId == id) ?? null;
                    if (FindDelivery != null)
                    {
                        FindDelivery.IsPickedUpForReturn = true;
                        FindDelivery.PickUpReturnDate = DateTime.Now;
                        core.Entry(FindDelivery).State = EntityState.Modified;
                        core.SaveChanges();
                        var FindEval = core.Evaluations.FirstOrDefault(x => x.DeliveryId == FindDelivery.DeliveryId) ?? null;
                        if (FindEval != null)
                        {
                            FindEval.DeliveryPickUpForReturn = true;
                            core.Entry(FindEval).State = EntityState.Modified;
                            core.SaveChanges();
                        }
                        message = string.Format($" Delivery Pick Up is successful  at {DateTime.Now.ToString("dd-MMMM-yyyy")}");
                    }
                }
                catch (Exception)
                {

                    // throw;
                }
                return RedirectToAction("PickUpOrder", new { message = message });
            }
        }
        // GET: Sales/Create
        public ActionResult Create()
        {
            return View();
        }

        // POST: Sales/Create
        // To protect from overposting attacks, enable the specific properties you want to bind to, for 
        // more details see https://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Create([Bind(Include = "SaleId,SaleDate,CustomerName,Email,Address,City,State,PostalCode,Country,PhoneNumber,SaleTotal")] Sale sale, ApplicationUser applicationUser)
        {
            sale.Email = User.Identity.Name;
            sale.Name = applicationUser.UserName;
            sale.Address = User.Identity.GetAddress();
            sale.City = User.Identity.GetCity();
            sale.State = User.Identity.GetState();
            sale.PostalCode = User.Identity.GetPostalCode();
            sale.Country = User.Identity.GetCountry();
            sale.PhoneNumber = User.Identity.GetPhoneNo();

            //Tracking statues
            sale.Dispatched = false;
            sale.ConfirmOrder = null;
            sale.ConfirmDelivery = false;

            if (ModelState.IsValid)
            {
                db.Sales.Add(sale);
                await db.SaveChangesAsync();
                return RedirectToAction("Index");
            }

            return View(sale);
        }

        // GET: Sales/Edit/5
        public async Task<ActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            Sale sale = await db.Sales.FindAsync(id);
            if (sale == null)
            {
                return HttpNotFound();
            }
            return View(sale);
        }

        // POST: Sales/Edit/5
        // To protect from overposting attacks, enable the specific properties you want to bind to, for 
        // more details see https://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> Edit([Bind(Include = "SaleId,SaleDate,CustomerName,Email,Address,City,State,PostalCode,Country,PhoneNumber,SaleTotal")] Sale sale)
        {
            if (ModelState.IsValid)
            {
                db.Entry(sale).State = EntityState.Modified;
                await db.SaveChangesAsync();
                return RedirectToAction("Index");
            }
            return View(sale);
        }

        // GET: Sales/Delete/5
        public async Task<ActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            Sale sale = await db.Sales.FindAsync(id);
            if (sale == null)
            {
                return HttpNotFound();
            }
            return View(sale);
        }

        // POST: Sales/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> DeleteConfirmed(int id)
        {
            Sale sale = await db.Sales.FindAsync(id);
            db.Sales.Remove(sale);
            await db.SaveChangesAsync();
            return RedirectToAction("Index");
        }



        public ActionResult Returns(int? id, int? ProductionId)
        {
            StatusHelper.CheckStatus();
            int DelId = 0;
            int SaleId = 0;
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            Sale sale = db.Sales.Find(id);
            if (sale == null)
            {
                return HttpNotFound();
            }

            var productList = from db in db.SaleDetails
                              where db.SaleId == id && db.ProductId == ProductionId
                              select db.Products;


            var deliverys = from db in db.Deliveries
                            where db.SaleId == id
                            select db;

            ViewBag.SaleDetails = productList;

            foreach (var item in deliverys)
            {
                DelId = item.DeliveryId;
            }

            Delivery delivery = db.Deliveries.Find(DelId);
            ViewBag.Date = delivery.DeliveryDate;
            ViewBag.Delivery = delivery.CurrentLocation;


            ReturnRefundVM returnRefundVM = new ReturnRefundVM()
            {
                delivery = delivery,
                sale = sale,
                ProducId = ProductionId
            };
            ViewBag.ReasonId = new SelectList(db.ReasonDrops, "Id", "Name");
            ViewBag.RNRId = new SelectList(db.RNRDatas.Where(x => x.Name == "Refund" || x.Name == "Return").ToList(), "Id", "Name");
            // ViewBag.OrderId = new SelectList(db.Sales, "SaleId", "SaleDate");
            ViewBag.StatusId = new SelectList(db.Statuses, "Id", "Name");
            return View(returnRefundVM);
        }

        [HttpPost]
        public ActionResult Returns(ReturnRefundVM returnRefundVM)
        {
            string message = string.Empty;
            using (ApplicationDbContext core = new ApplicationDbContext())
            {

                var CheckIfAlreadyPending = core.ReturnAndRefunds.FirstOrDefault(x => x.OrderId == returnRefundVM.sale.SaleId && x.CustomerName == User.Identity.Name && x.ProductionId == returnRefundVM.ProducId) ?? null;
                if (CheckIfAlreadyPending == null)
                {
                    returnRefundVM.ReturnAndRefund.OrderId = returnRefundVM.sale.SaleId;
                    returnRefundVM.ReturnAndRefund.RNRId = returnRefundVM.RNRId;
                    returnRefundVM.ReturnAndRefund.ReasonId = returnRefundVM.ReasonId;
                    returnRefundVM.ReturnAndRefund.ProductionId = returnRefundVM.ProducId;
                    returnRefundVM.ReturnAndRefund.CustomerName = User.Identity.Name;
                    returnRefundVM.ReturnAndRefund.ReferenceNumber = ReturnAndRefundsController.GenerateReferenceNumber();


                    core.ReturnAndRefunds.Add(returnRefundVM.ReturnAndRefund);
                    core.SaveChanges();
                    message = string.Format($"Request for return saved sucessfully, {DateTime.Now.ToString("yyyy-MM-dd")}");
                }
                else
                {
                    message = string.Format($"Request for return already created , unable to create another, {DateTime.Now.ToString("yyyy-MM-dd")}");
                }




            }
            return RedirectToAction("Details", "Sales", new
            {
                id = returnRefundVM.sale.SaleId,
                message = message
            });
        }

        public JsonResult Addfeedback(int id, string comment, int rating, string Bookname)
        {
            using (ApplicationDbContext core = new ApplicationDbContext())
            {
                var bookid = (from f in core.Feedbacks
                              join s in core.Sales on f.SaleId equals s.SaleId
                              join sd in core.SaleDetails on s.SaleId equals sd.SaleId
                              join p in core.Products on sd.ProductId equals p.ProductId
                              select p).FirstOrDefault();

                Feedback feedback = new Feedback();
                feedback.Created = DateTime.Now;
                feedback.BookId = bookid?.ProductId;
                feedback.SaleId = id;
                feedback.Comment = comment;
                feedback.Rating = rating;
                feedback.Bookname = Bookname;
                core.Feedbacks.Add(feedback);

                Analytic analytic = new Analytic();




                core.SaveChanges();
                return Json(true, JsonRequestBehavior.AllowGet);
            }
        }

        public ActionResult Feedback(int? id)
        {
            using (ApplicationDbContext core = new ApplicationDbContext())
            {
                var list = (from feedback in core.Feedbacks
                            join s in core.Sales on feedback.SaleId equals s.SaleId
                            join sd in core.SaleDetails on s.SaleId equals sd.SaleId
                            join p in core.Products on sd.ProductId equals p.ProductId
                            select new FeedbackVM
                            {
                                BookId = id,
                                Bookname = p.ProductName,
                                Comment = feedback.Comment,
                                Rating = feedback.Rating
                            }).ToList();


                return View(list);
            }


        }

        [HttpPost]
        public ActionResult SaveRating(int? Id, SaleDetail model, String Comment, int Rating)
        {
            return View();
        }


        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}