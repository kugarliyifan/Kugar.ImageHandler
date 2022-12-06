using System.ComponentModel.DataAnnotations;
using System.Xml;
using Kugar.Core.ExtMethod;
using Kugar.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.StaticFiles;
using NetVips;
using static System.Net.Mime.MediaTypeNames;
using Image = NetVips.Image;

namespace Kugar.ImageHandler.Controllers
{

    public class ImageController : ControllerBase
    {
        static ImageController()
        {
            _provider= new FileExtensionContentTypeProvider();
            _provider.Mappings[".apk"] = "application/vnd.android.package-archive";
            
            var webConfigPath = Path.Join(Directory.GetCurrentDirectory(), "web.config");

            if (System.IO.File.Exists(webConfigPath))
            {
                var xmlDoc = new XmlDocument();
                
                using (var file = System.IO.File.Open(webConfigPath, FileMode.Open, FileAccess.Read))
                {
                    xmlDoc.Load(file);
                }
                
                var node = xmlDoc.GetFirstElementsByTagName("staticContent");

                if (node!=null)
                {
                    var mimeNodes=node.GetElementsByTagName("mimeMap");

                    if (mimeNodes.HasData())
                    {
                        foreach (var item in mimeNodes)
                        {
                            var ext = item.GetAttribute("fileExtension");
                            var mime = item.GetAttribute("mimeType");

                            if (!_provider.Mappings.ContainsKey(ext))
                            {
                                _provider.Mappings.Add(ext,mime);
                            }
                         
                        }
                    }
                }

                
            } 


        }

        [Route("uploads/{*path}")]
        [HttpGet]
        [ResponseCache(Location = ResponseCacheLocation.Any,Duration =3600*24*2,VaryByQueryKeys = new string[]{"w","h"} )]
        [OutputCache(Duration = 3600*24*2,VaryByQueryKeys = new string[]{"w","h"},PolicyName = "imagecache")]
        public async Task<IActionResult> Index([FromRoute, Required] string path, [FromQuery, Required] int? w, [FromQuery] int? h = null,[FromServices]IStorage storage=null)
        {
            var ext = Path.GetExtension(path);

            if (!_provider.Mappings.ContainsKey(ext))
            {
                return NotFound();
            }

            var result = await storage.ReadFileAsync(path);

            if (result.IsSuccess)
            {
                var data = result.ReturnData; 
                if (isImage(ext) && w.HasValue)
                {
                    using var image =  NetVips.Image.NewFromStream(data); 

                    if (  image.Width>w || (h.HasValue && image.Height>h)) 
                    { 
                        using var newImg = image.ThumbnailImage(width: w.Value, height: h);

                        var buff = newImg.WriteToBuffer(ext);
                         
                        await data.DisposeAsync();
                        return new FileContentResult(buff,_provider.Mappings[ext]);
                    }
                    else
                    {
                        data.Position = 0;
                        
                        image.Close();
                        image.Dispose();
                        return new FileStreamResult(data, _provider.Mappings[ext]);
                    }
                }
                else
                {
                    return new FileStreamResult(data, _provider.Mappings[ext]);
                    //return File(data, _provider.Mappings[ext]);
                }
                
            }
            else
            {
                return NotFound();
            } 
        }

        public static FileExtensionContentTypeProvider _provider = null;
        private static HashSet<string> _imageHashExt=new HashSet<string>(StringComparer.CurrentCultureIgnoreCase)
        {
            ".jpg",
            ".jpeg",
            ".bmp",
            ".png",
            ".webp"
        };

        private bool isImage(string ext)
        {
            return _imageHashExt.Contains(ext);
        }
    }


    public class NetVipsImageResult : IActionResult
    {
        /// <summary>
        /// 直接输出Bitmap格式
        /// </summary>
        /// <param name="image">图片数据</param>
        /// <param name="format">输出的文件格式,默认为jpg</param>
        /// <param name="autoDispose">输出完成后,是否自动释放掉bitmap,默认为true</param>
        public NetVipsImageResult(Image image, string ext, bool autoDispose = true)
        {
            if (image == null)
            {
                throw new ArgumentNullException("image", "输入图像不能为空");
            }

            AutoDisposeImage = autoDispose;

            ImageData = image;
            Ext = ext;
        }

        /// <summary>
        ///  图片数据
        /// </summary>
        public Image ImageData { set; get; }

        public string Ext { set; get; }


        /// <summary>
        /// 使用后自动释放图像对象
        /// </summary>
        public bool AutoDisposeImage { set; get; }



        // 主要需要重写的方法
        public async Task ExecuteResultAsync(ActionContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }

            var resp = context.HttpContext.Response;

            resp.Clear();

            //resp.Headers.CacheControl = "max-age=" + TimeSpan.FromDays(10).TotalSeconds;

            //var e = Ext.Substring(1);


            // 设置 HTTP Header
            resp.ContentType = ImageController._provider.Mappings[Ext];// "image/" + (e=="jpg"?"jpeg":e);

            //var buff=ImageData.WriteToBuffer(Ext);
             
            //await resp.Body.WriteAsync(buff);

            //await resp.Body.FlushAsync(); 

            //var stream = resp.BodyWriter.AsStream();

            using (var memory=new MemoryStream())
            {
                ImageData.WriteToStream(memory, Ext );

                await memory.FlushAsync();

                ImageData.Dispose();

                await memory.CopyToAsync(resp.Body);

                await resp.Body.FlushAsync();
            }
             
            
        }
    }

    public class ResponseTarget : TargetCustom
    {
        private HttpResponse _response = null;

        public ResponseTarget(HttpResponse response)
        {
            _response = response;

            this.OnWrite += ResponseTarget_OnWrite;
            this.OnEnd += ResponseTarget_OnEnd;
                
        }

        private int ResponseTarget_OnEnd()
        {
            _response.Body.Flush();

            return 0;
        }

        private long ResponseTarget_OnWrite(byte[] buffer, int length)
        {
            _response.Body.Write(buffer,0,length);

            return length;
        }

        public void CloseResp()
        {
            this.OnWrite -= ResponseTarget_OnWrite;
            this.OnEnd-= ResponseTarget_OnEnd;
        }
    }
}
