﻿using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Discussion.Core.Data;
using Discussion.Core.FileSystem;
using Discussion.Core.Models;
using Discussion.Tests.Common;
using Discussion.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Moq;
using Xunit;

namespace Discussion.Web.Tests.Specs.Controllers
{
    [Collection("WebSpecs")]
    public class CommonControllerSpecs
    {
        private readonly TestDiscussionWebApp _app;
        private readonly IRepository<FileRecord> _fileRepo;
        private IFileSystem _fs;

        public CommonControllerSpecs(TestDiscussionWebApp app)
        {
            _app = app.Reset();
            _fileRepo = _app.GetService<IRepository<FileRecord>>();
            _fs = _app.GetService<IFileSystem>();
        }

        [Fact]
        public void convert_markdown_to_html()
        {
            // Act
            var commonController = _app.CreateController<CommonController>();

            // Action
            dynamic htmlFromMd = commonController.RenderMarkdown("# Title");

            Assert.Equal("<h2>Title</h2>\n",  htmlFromMd.Html);
        }
        
        
        [Fact]
        public async Task should_upload_file()
        {
            _app.MockUser();
            var file = MockFile("test-file.txt");
          
            var commonController = _app.CreateController<CommonController>();
            commonController.Url = CreateMockUrlHelper("file-link");
            var requestResult = await commonController.UploadFile(file, "avatar");

            Assert.NotNull(requestResult);
            Assert.True(requestResult.HasSucceeded);
            
            dynamic uploadResult = requestResult.Result;
            int fileId = uploadResult.FileId;
            var fileRecord = _fileRepo.Get(fileId);
            Assert.Equal("avatar",fileRecord.Category);
            Assert.Equal("test-file.txt",fileRecord.OriginalName);
            
            string publicUrl = uploadResult.PublicUrl;
            Assert.Equal("file-link", publicUrl);
        }
        
        [Fact]
        public async Task should_download_file()
        {
            const string fileContent = "Hello World from a Fake File 其中还包含中文";
            
            var appUser = _app.MockUser();
            var storageFile = await _fs.CreateFileAsync("testing/the-file.txt");
            long fileLength;

            using (var ms = new MemoryStream())
            {
                var writer = new StreamWriter(ms);
                writer.Write(fileContent);
                writer.Flush();
                ms.Seek(0, SeekOrigin.Begin);
                fileLength = ms.Length;

                using (var dest = await storageFile.OpenWriteAsync())
                {
                    await ms.CopyToAsync(dest);
                }
            }

            var fileRecord = new FileRecord
            {
                OriginalName = "the-file.txt",
                Category = "testing",
                UploadedBy = appUser.Id,
                StoragePath = storageFile.GetPath(),
                Size = fileLength,
                Slug = Guid.NewGuid().ToString("N")
            };
            _fileRepo.Save(fileRecord);
          
            var commonController = _app.CreateController<CommonController>();
            var downloadResult = await commonController.DownloadFile(fileRecord.Slug, download: true) as FileStreamResult;

            Assert.NotNull(downloadResult);
            Assert.Equal(fileRecord.OriginalName, downloadResult.FileDownloadName);
            Assert.Equal(fileLength, downloadResult.FileStream.Length);
            using (var reader = new StreamReader(downloadResult.FileStream))
            {
                var content = await reader.ReadToEndAsync();
                Assert.Equal(fileContent, content);
            }
        }

        private static IFormFile MockFile(string fileName)
        {
            var fileMock = new Mock<IFormFile>();
            var ms = new MemoryStream();
            var writer = new StreamWriter(ms);
            writer.Write("Hello World from a Fake File");
            writer.Flush();
            ms.Seek(0, SeekOrigin.Begin);

            fileMock.Setup(_ => _.OpenReadStream()).Returns(ms);
            fileMock.Setup(_ => _.FileName).Returns(fileName);
            fileMock.Setup(_ => _.Length).Returns(ms.Length);
            return fileMock.Object;
        }
        
        private static IUrlHelper CreateMockUrlHelper(string fileLink)
        {
            var urlHelper = new Mock<IUrlHelper>();
            urlHelper.Setup(url => url.Action(It.IsAny<UrlActionContext>())).Returns(fileLink);
            return urlHelper.Object;
        }
        [Fact]
        public async Task should_down_file_server()
        {
            try
            {
                ///模拟http下载文件请求，分别附加request headers If-Modified-Since If-None-Match 
                ///第一次请求什么都不带 返回报文中检查响应头
                ///第二次请求根据响应头中的时间和token验证，正确返回缓存 httostatuscode304
                ///第三次请求根据响应头中正确时间和错误的token,返回下载文件 httostatuscode200
                var client = new HttpClient();
                var response = await client.GetAsync("https://localhost:5021/api/common/download/be8f02ca8fd44d0dbbc76513b6221a9f");
                response.Headers.ETag.Tag.Equals("1c1b4216127d046");
                //response.Headers.
                string responseStatusCode = response.StatusCode.ToString();
                Assert.Equal("304", responseStatusCode);
                client.DefaultRequestHeaders.Add("If-Modified-Sinc", "");
                client.DefaultRequestHeaders.Add("If-None-Match", "");
                response = await client.GetAsync("https://localhost:5021/api/common/download/be8f02ca8fd44d0dbbc76513b6221a9f");
                client.DefaultRequestHeaders.Add("If-Modified-Sinc", "");
                client.DefaultRequestHeaders.Add("If-None-Match", "");
                response = await client.GetAsync("https://localhost:5021/api/common/download/be8f02ca8fd44d0dbbc76513b6221a9f");
            }
            catch(Exception ex)
            {
                Console.WriteLine(ex.Message);
            }


        }
    } 
}