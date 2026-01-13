using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using System.Web.Helpers;
using System.Web.Mvc;
using ECFood.Models;
using Microsoft.Win32;
using System.IO; // <--- 加這行
using static System.ActivationContext;
using System.Web.UI.WebControls;
using System.Data.Entity;

namespace ECFood.Controllers
{
    public class HomeController : Controller
    {

        public DatabaseEntities db = new DatabaseEntities();

        //後台首頁
        public ActionResult A_home()
        {
            var today = DateTime.Today;
            var startDate = today.AddDays(-29); // 30 天（含今天）

            // 總計與今日新增
            ViewBag.TotalUsers = db.Users.Count();
            ViewBag.TodayNewUsers = db.Users.Count(u => DbFunctions.TruncateTime(u.RegisterDate) == today);

            ViewBag.TotalRecipes = db.Recipes.Count();
            ViewBag.TodayNewRecipes = db.Recipes.Count(r => DbFunctions.TruncateTime(r.CreatedDate) == today);

            // 產生連續 30 天日期清單
            var dateRange = Enumerable.Range(0, 30)
                .Select(i => startDate.AddDays(i))
                .ToList();

            // 用戶註冊趨勢
            var userRaw = db.Users
                .Where(u => u.RegisterDate >= startDate)
                .GroupBy(u => DbFunctions.TruncateTime(u.RegisterDate))
                .Select(g => new { Date = g.Key, Count = g.Count() })
                .ToList();

            ViewBag.UserTrend = dateRange
                .Select(d => new {
                    Date = d.ToString("MM-dd"),
                    Count = userRaw.FirstOrDefault(x => x.Date == d)?.Count ?? 0
                })
                .ToList();

            // 食譜上傳趨勢
            var recipeRaw = db.Recipes
                .Where(r => r.CreatedDate >= startDate)
                .GroupBy(r => DbFunctions.TruncateTime(r.CreatedDate))
                .Select(g => new { Date = g.Key, Count = g.Count() })
                .ToList();

            ViewBag.RecipeTrend = dateRange
                .Select(d => new {
                    Date = d.ToString("MM-dd"),
                    Count = recipeRaw.FirstOrDefault(x => x.Date == d)?.Count ?? 0
                })
                .ToList();

            // 收藏趨勢
            var favRaw = db.Favorites
                .Where(f => f.FavoritedDate >= startDate)
                .GroupBy(f => DbFunctions.TruncateTime(f.FavoritedDate))
                .Select(g => new { Date = g.Key, Count = g.Count() })
                .ToList();

            ViewBag.FavoriteTrend = dateRange
                .Select(d => new {
                    Date = d.ToString("MM-dd"),
                    Count = favRaw.FirstOrDefault(x => x.Date == d)?.Count ?? 0
                })
                .ToList();

            // 留言趨勢 + 評分折線
            var commentRaw = db.Comments
                .Where(c => c.CommentDate >= startDate)
                .GroupBy(c => DbFunctions.TruncateTime(c.CommentDate))
                .Select(g => new {
                    Date = g.Key,
                    Count = g.Count(),
                    Avg = g.Average(x => (double?)x.Rating) ?? 0
                })
                .ToList();

            ViewBag.CommentTrend = dateRange
                .Select(d => new {
                    Date = d.ToString("MM-dd"),
                    Count = commentRaw.FirstOrDefault(x => x.Date == d)?.Count ?? 0
                })
                .ToList();

            ViewBag.AvgRatingTrend = dateRange
                .Select(d => new {
                    Date = d.ToString("MM-dd"),
                    Avg = Math.Round(commentRaw.FirstOrDefault(x => x.Date == d)?.Avg ?? 0, 2)
                })
                .ToList();

            return View();
        }



        //員工管理
        public ActionResult A_users(string search)
        {
            var users = db.Users.AsQueryable();
            if (!string.IsNullOrEmpty(search))
                users = users.Where(u => u.UserName.Contains(search));
            return View(users.ToList());
        }

        public ActionResult EditUserPage(int id)
        {
            var user = db.Users.Find(id);
            if (user == null) return HttpNotFound();
            return View(user);
        }

        [HttpPost]
        public ActionResult EditUserPage(Users updatedUser)
        {
            var user = db.Users.Find(updatedUser.UserId);
            if (user != null)
            {
                user.UserName = updatedUser.UserName;
                user.Email = updatedUser.Email;
                user.Password = updatedUser.Password;
                user.Role = updatedUser.Role;
                db.SaveChanges();
                TempData["Success"] = "會員資訊已更新成功！";
            }
            else
            {
                TempData["Error"] = "找不到該使用者，更新失敗。";
            }
            return RedirectToAction("A_users");
        }

        [HttpPost]
        public JsonResult AjaxDeleteUser(int id)
        {
            var user = db.Users.Find(id);
            if (user != null)
            {
                db.Users.Remove(user);
                db.SaveChanges();
                return Json(new { success = true });
            }
            return Json(new { success = false });
        }

        public ActionResult AddUserPage()
        {
            return View();
        }

        [HttpPost]
        public ActionResult AddUserPage(Users newUser)
        {
            if (ModelState.IsValid)
            {
                if (!db.Users.Any(u => u.Email == newUser.Email))
                {
                    newUser.RegisterDate = DateTime.Now;
                    db.Users.Add(newUser);
                    db.SaveChanges();
                    TempData["Success"] = "新增會員成功！";
                }
                else
                {
                    TempData["Error"] = "此 Email 已被註冊。";
                }
                return RedirectToAction("A_users");
            }
            TempData["Error"] = "資料格式有誤，請重新填寫。";
            return RedirectToAction("A_users");
        }

        //食譜管理
        public ActionResult A_recipes()
        {
            var recipes = db.Recipes.ToList();

            // 分類下拉選單
            ViewBag.Categories = db.Categories
                .Select(c => new SelectListItem
                {
                    Value = c.CategoryId.ToString(),
                    Text = c.Name
                })
                .ToList();

            // YouTube 影片 ID 對照表（給 View 顯示縮圖用）
            ViewBag.YoutubeIds = recipes.ToDictionary(
                r => r.RecipeId,
                r => GetYoutubeVideoId1(r.YoutubeUrl)
            );

            return View(recipes);
        }

        private string GetYoutubeVideoId1(string youtubeUrl)
        {
            if (string.IsNullOrEmpty(youtubeUrl)) return "";

            // 支援常見格式：watch?v=、youtu.be/、embed/
            var regex = new System.Text.RegularExpressions.Regex(@"(?:v=|be/|embed/)([\w-]{11})");
            var match = regex.Match(youtubeUrl);

            if (match.Success)
                return match.Groups[1].Value;

            // 備援：直接是一組 ID（11 碼）
            return youtubeUrl.Length == 11 ? youtubeUrl : "";
        }

        [HttpPost]
        public JsonResult TogglePublish(int id)
        {
            var recipe = db.Recipes.Find(id);
            if (recipe != null)
            {
                recipe.IsPublished = !recipe.IsPublished;
                db.SaveChanges();
                return Json(new { success = true });
            }
            return Json(new { success = false });
        }


        [HttpPost]
        public JsonResult AjaxDeleteRecipe(int id)
        {
            var recipe = db.Recipes.Find(id);
            if (recipe != null)
            {
                db.Recipes.Remove(recipe);
                db.SaveChanges();
                return Json(new { success = true });
            }
            return Json(new { success = false });
        }


        //意見箱
        public ActionResult A_feedbacks(DateTime? startDate, DateTime? endDate)
        {
            var query = db.ContactForms.AsQueryable();

            if (startDate.HasValue)
            {
                query = query.Where(f => f.SubmitDate >= startDate.Value);
            }

            if (endDate.HasValue)
            {
                // 包含整天：加一天再扣掉一毫秒
                var inclusiveEnd = endDate.Value.AddDays(1).AddMilliseconds(-1);
                query = query.Where(f => f.SubmitDate <= inclusiveEnd);
            }

            var results = query
                .OrderByDescending(f => f.SubmitDate)
                .ToList();

            return View(results);
        }


        public ActionResult Register()
        {
            return View();
        }
        [HttpPost]
        public ActionResult Register(string UserName, string Email, string Password, string ConfirmPassword)
        {
            if (ModelState.IsValid)
            {
                // 驗證是否密碼一致
                if (Password != ConfirmPassword)
                {
                    ModelState.AddModelError("ConfirmPassword", "兩次密碼輸入不一致");
                    return View();
                }

                // 驗證 Email 是否已存在
                if (db.Users.Any(u => u.Email == Email))
                {
                    ModelState.AddModelError("Email", "此 Email 已被註冊");
                    return View();
                }

                // 建立新使用者物件
                var newUser = new Users
                {
                    UserName = UserName,
                    Email = Email,
                    Password = Password, // 密碼加密
                    RegisterDate = DateTime.Now,
                    Role = "一般會員"
                };

                db.Users.Add(newUser);
                db.SaveChanges();

                // 註冊成功導向登入或首頁
                return RedirectToAction("Login");
            }

            // 若表單驗證失敗則回傳原畫面
            return View();
        }

        public ActionResult Login()
        {
            return View();
        }

        // 處理登入提交
        [HttpPost]
        public ActionResult Login(string Email, string Password)
        {
            if (ModelState.IsValid)
            {
                var user = db.Users.FirstOrDefault(u => u.Email == Email && u.Password == Password);

                if (user != null)
                {
                    // 儲存登入者資訊到 Session
                    Session["UserId"] = user.UserId;
                    Session["UserName"] = user.UserName;
                    Session["UserRole"] = user.Role;

                    // 根據角色導向不同頁面
                    if (user.Role == "管理者")
                    {
                        return RedirectToAction("A_home", "Home");
                    }
                    else // 一般會員
                    {
                        return RedirectToAction("Index", "Home");
                    }
                }

                ModelState.AddModelError("", "帳號或密碼錯誤");
            }

            return View();
        }

        // 登出
        public ActionResult Logout()
        {
            Session.Clear(); // 清空所有 Session 資料
            return RedirectToAction("Login");
        }


        public ActionResult MyRecipes()
        {
            int currentUserId = Convert.ToInt32(Session["UserId"]);

            var myRecipes = db.Recipes.Where(r => r.UserId == currentUserId).ToList();

            // 產生一個字典來存每個 Recipe 對應的 YoutubeId
            var youtubeIds = new Dictionary<int, string>();

            foreach (var recipe in myRecipes)
            {
                try
                {
                    var uri = new Uri(recipe.YoutubeUrl);
                    var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                    var youtubeId = query["v"];
                    youtubeIds[recipe.RecipeId] = youtubeId;
                }
                catch
                {
                    youtubeIds[recipe.RecipeId] = ""; // 錯誤處理：保險起見
                }
            }

            ViewBag.YoutubeIds = youtubeIds;
            ViewBag.Categories = db.Categories.ToList();

            return View(myRecipes);
        }


        // 新增食譜
        [HttpPost]
        public ActionResult Recipe_Add(Recipes model, HttpPostedFileBase ImageFile)
        {
            model.UserId = Convert.ToInt32(Session["UserId"]);
            model.CreatedDate = DateTime.Now;
            if (ImageFile != null && ImageFile.ContentLength > 0)
            {
                var fileName = Path.GetFileName(ImageFile.FileName);
                var savePath = Path.Combine(Server.MapPath("~/Images"), fileName);
                ImageFile.SaveAs(savePath);
                model.ImageUrl = "/Images/" + fileName;
            }

            db.Recipes.Add(model);
            db.SaveChanges();

            return RedirectToAction("MyRecipes");
        }

        // 修改食譜
        [HttpPost]
        public ActionResult Recipe_Edit(Recipes model, HttpPostedFileBase ImageFile)
        {
            var recipe = db.Recipes.Find(model.RecipeId);
            if (recipe != null)
            {
                recipe.Title = model.Title;
                recipe.Description = model.Description;
                recipe.YoutubeUrl = model.YoutubeUrl;
                recipe.Ingredients = model.Ingredients;
                recipe.Steps = model.Steps;
                recipe.Difficulty = model.Difficulty;
                recipe.CookTime = model.CookTime;
                recipe.CategoryId = model.CategoryId;
                recipe.IsPublished = model.IsPublished;
                recipe.IsPublished = model.IsPublished;

                
            }
            if (ImageFile != null && ImageFile.ContentLength > 0)
            {
                string fileName = Path.GetFileName(ImageFile.FileName);
                string path = Path.Combine(Server.MapPath("~/Images"), fileName);
                ImageFile.SaveAs(path);
                recipe.ImageUrl = "/Images/" + fileName;
            }
            db.SaveChanges();
            return RedirectToAction("MyRecipes");
        }

        // 刪除食譜
        [HttpPost]
        public ActionResult Recipe_Delete(int RecipeId)
        {
            var recipe = db.Recipes.Find(RecipeId);
            if (recipe != null)
            {
                db.Recipes.Remove(recipe);
                db.SaveChanges();
            }

            return RedirectToAction("MyRecipes");
        }


        public ActionResult RecipeBrowse(string keyword, int? categoryId)
        {
            // 所有公開食譜
            var query = db.Recipes
                .Where(r => r.IsPublished);

            // 關鍵字篩選
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                query = query.Where(r => r.Title.Contains(keyword));
            }

            // 分類篩選
            if (categoryId.HasValue)
            {
                query = query.Where(r => r.CategoryId == categoryId.Value);
            }

            // 結果
            var recipes = query.ToList();

            // 分類選單用
            ViewBag.Categories = db.Categories.ToList();

            // YouTube 縮圖用
            ViewBag.YoutubeIds = recipes.ToDictionary(
                r => r.RecipeId,
                r => GetYoutubeVideoId(r.YoutubeUrl)
            );

            // 收藏數量
            var favoriteCounts = recipes.ToDictionary(
                r => r.RecipeId,
                r => GetFavoriteCount(r.RecipeId)
            );
            ViewBag.FavoriteCounts = favoriteCounts;

            // ⭐️ 加上平均評分資料（給星星用）
            var averageRatings = recipes.ToDictionary(
                r => r.RecipeId,
                r =>
                {
                    var ratings = db.Comments
                        .Where(c => c.RecipeId == r.RecipeId && c.Rating != null)
                        .Select(c => c.Rating.Value);
                    return ratings.Any() ? Math.Round(ratings.Average(), 1) : 0;
                }
            );
            ViewBag.AverageRatings = averageRatings;

            // 登入者的收藏列表（用來顯示按鈕狀態）
            int? userId = Session["UserId"] as int?;
            var favoritedRecipeIds = new List<int>();

            if (userId != null)
            {
                favoritedRecipeIds = db.Favorites
                    .Where(f => f.UserId == userId)
                    .Select(f => f.RecipeId)
                    .ToList();
            }

            ViewBag.FavoritedIds = favoritedRecipeIds;

            return View(recipes);
        }


        [HttpPost]
        public JsonResult ToggleFavorite(int recipeId)
        {
            int? userId = Session["UserId"] as int?;
            if (userId == null)
            {
                return Json(new { success = false, message = "請先登入" });
            }

            var favorite = db.Favorites
                .FirstOrDefault(f => f.UserId == userId && f.RecipeId == recipeId);

            bool isNowFavorite;

            if (favorite != null)
            {
                // 已收藏 → 移除
                db.Favorites.Remove(favorite);
                isNowFavorite = false;
            }
            else
            {
                // 未收藏 → 加入
                db.Favorites.Add(new Favorites
                {
                    UserId = userId.Value,
                    RecipeId = recipeId,
                    FavoritedDate = DateTime.Now
                });
                isNowFavorite = true;
            }

            db.SaveChanges();
       


            return Json(new { success = true, isFavorite = isNowFavorite });
        }



        // 處理youtube首頁圖片
        private string GetYoutubeVideoId(string youtubeUrl)
        {
            if (string.IsNullOrEmpty(youtubeUrl)) return "";

            var uri = new UriBuilder(youtubeUrl).Uri;
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            return query["v"] ?? ""; // https://www.youtube.com/watch?v=xxx
        }


        // 顯示 "關於我們" 頁面
        public ActionResult AboutUs()
        {
            return View();
        }

        // 處理 "聯絡我們" 表單提交
        [HttpPost]
        public ActionResult SubmitContactForm(ContactForms model)
        {
            if (ModelState.IsValid)
            {
            
                    // 創建 ContactForm 實例並設置其屬性
                    var contactForm = new ContactForms
                    {
                        UserId = 1,  // 假設你有 UserId 邏輯，這裡硬編碼為 1
                        Name = model.Name,
                        Email = model.Email,
                        Message = model.Message,
                        SubmitDate = DateTime.Now
                    };

                    // 使用 Entity Framework 新增資料
                    db.ContactForms.Add(contactForm);
                    db.SaveChanges(); // 儲存變更至資料庫
                

                // 表單提交成功後重定向到感謝頁面
                return RedirectToAction("ThankYou");
            }

            // 如果表單驗證失敗，則返回聯絡表單頁面
            return View("AboutUs");
        }


        // 感謝頁面
        public ActionResult ThankYou()
        {
            return View();
        }



        // 顯示收藏數量
        public int GetFavoriteCount(int recipeId)
        {
            return db.Favorites.Count(f => f.RecipeId == recipeId);
        }



        //詳細食譜
        public ActionResult RecipeDetails(int id)
        {
            var recipe = db.Recipes.Find(id);
            var comments = db.Comments
                             .Where(c => c.RecipeId == id)
                             .OrderByDescending(c => c.CommentDate)
                             .ToList();
            var category = db.Categories.FirstOrDefault(c => c.CategoryId == recipe.CategoryId);
            ViewBag.CategoryName = category?.Name ?? "未知";


            int? userId = Session["UserId"] as int?;
            bool isFavorited = false;

            if (userId != null)
            {
                isFavorited = db.Favorites.Any(f => f.UserId == userId && f.RecipeId == id);
            }

            ViewBag.Comments = comments;
            ViewBag.YoutubeId = GetYoutubeVideoId(recipe.YoutubeUrl);
            ViewBag.IsFavorited = isFavorited;


            return View(recipe);
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult AddComment(Comments comment)
        {
            int currentUserId = Convert.ToInt32(Session["UserId"]);
            comment.UserId = currentUserId; // 取得 UserId
            comment.CommentDate = DateTime.Now;

            db.Comments.Add(comment);
            db.SaveChanges();
            return RedirectToAction("RecipeDetails", "Home", new { id = comment.RecipeId });

        }

        public ActionResult Favorites(string keyword = null, int? categoryId = null)
        {
            var userId = Session["UserId"] as int?;
            if (userId == null)
            {
                return RedirectToAction("Login", "Home");
            }

            // 取得用戶收藏的食譜 ID
            var favoritedIds = db.Favorites
                                 .Where(f => f.UserId == userId)
                                 .Select(f => f.RecipeId)
                                 .ToList();

            // 撈出已收藏且符合條件的食譜
            var recipes = db.Recipes
                            .Where(r => r.IsPublished && favoritedIds.Contains(r.RecipeId))
                            .Where(r => string.IsNullOrEmpty(keyword) || r.Title.Contains(keyword))
                            .Where(r => !categoryId.HasValue || r.CategoryId == categoryId)
                            .ToList();

            // 收藏數資訊
            var favoriteCounts = db.Favorites
                                   .GroupBy(f => f.RecipeId)
                                   .ToDictionary(g => g.Key, g => g.Count());

            // Youtube ID 映射（假設資料中有這欄）
            // var youtubeIds = recipes.ToDictionary(r => r.RecipeId, r => r.YoutubeVideoId);
            var youtubeIds = recipes.ToDictionary(
                 r => r.RecipeId,
                 r => GetYoutubeVideoId(r.YoutubeUrl)
             );

            //評價資訊
            var averageRatings = recipes.ToDictionary(
                r => r.RecipeId,
                r => {
                    var ratings = db.Comments.Where(c => c.RecipeId == r.RecipeId && c.Rating != null).Select(
                        c => c.Rating.Value);
                    return ratings.Any() ? Math.Round(ratings.Average(), 1) : 0;
                }
            );

            ViewBag.YoutubeIds = youtubeIds;
            ViewBag.FavoritedIds = favoritedIds;
            ViewBag.FavoriteCounts = favoriteCounts;
            ViewBag.AverageRatings = averageRatings;

            ViewBag.Categories = db.Categories.ToList();

            return View(recipes);
        }

        //首頁
        public ActionResult Index()
        {
            var allRecipes = db.Recipes.Where(r => r.IsPublished).ToList();

            // 收藏數量
            var favoriteCounts = allRecipes.ToDictionary(
                r => r.RecipeId,
                r => db.Favorites.Count(f => f.RecipeId == r.RecipeId)
            );

            // 評分平均
            var averageRatings = allRecipes.ToDictionary(
                r => r.RecipeId,
                r =>
                {
                    var ratings = db.Comments.Where(c => c.RecipeId == r.RecipeId && c.Rating != null).Select(c => c.Rating.Value);
                    return ratings.Any() ? Math.Round(ratings.Average(), 1) : 0;
                }
            );

            // YouTube ID
            var youtubeIds = allRecipes.ToDictionary(
                r => r.RecipeId,
                r => GetYoutubeVideoId(r.YoutubeUrl)
            );

            // 熱門（收藏最多）
            var mostFavorited = allRecipes
                .OrderByDescending(r => favoriteCounts[r.RecipeId])
                .Take(3)
                .ToList();

            // 最高評價
            var topRated = allRecipes
                .OrderByDescending(r => averageRatings[r.RecipeId])
                .Take(3)
                .ToList();

            // 最新上架
            var newest = allRecipes
                .OrderByDescending(r => r.CreatedDate)
                .Take(3)
                .ToList();

            // 傳到 ViewBag
            ViewBag.MostFavorited = mostFavorited;
            ViewBag.TopRated = topRated;
            ViewBag.Newest = newest;

            ViewBag.FavoriteCounts = favoriteCounts;
            ViewBag.AverageRatings = averageRatings;
            ViewBag.YoutubeIds = youtubeIds;

            return View();
        }


        public ActionResult About()
        {
            ViewBag.Message = "Your application description page.";

            return View();
        }

        public ActionResult Contact()
        {
            ViewBag.Message = "Your contact page.";

            return View();
        }
    }
}