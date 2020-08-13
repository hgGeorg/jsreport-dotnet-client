﻿using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using jsreport.Local;
using Shouldly;
using jsreport.Binary;
using jsreport.Types;
using Newtonsoft.Json.Serialization;

namespace jsreport.Client.Test
{
    [TestFixture]
    [SingleThreaded]
    public class ReportingServiceTest
    {
        private ReportingService _reportingService;
        private ILocalWebServerReportingService _localReportingService;        

        [SetUp]
        public async Task SetUp()
        {           
            _localReportingService = new LocalReporting().KillRunningJsReportProcesses().UseBinary(JsReportBinary.GetBinary()).AsWebServer().Create();
            await _localReportingService.StartAsync();            
            _reportingService = new ReportingService("http://localhost:5488");            
        }

        [TearDown]
        public async Task TearDown()
        {            
            await _localReportingService.KillAsync();            
        }

        [Test]
        public async Task ChromePdfTest()
        {
            var result = await _reportingService.RenderAsync(new
            {
                template = new
                {
                    content = "foo",
                    engine = "none",
                    recipe = "chrome-pdf"
                }
            });

            using (var reader = new StreamReader(result.Content))
            {
                reader.ReadToEnd().ShouldStartWith("%PDF");
            }
        }

        [Test]
        public async Task SerializationDataContractResolverTest()
        {
            _reportingService.ContractResolverForDataProperty = new CamelCasePropertyNamesContractResolver();
            var result = await _reportingService.RenderAsync(new
            {
                template = new
                {
                    content = "{{helloWorld}}",
                    engine = "handlebars",
                    recipe = "html"
                },
                data = new
                {
                    HelloWorld = "foo"
                }
            });

            using (var reader = new StreamReader(result.Content))
            {
                reader.ReadToEnd().ShouldStartWith("foo");
            }
        }


        [Test]
        public async Task HtmlTest()
        {
            var result = await _reportingService.RenderAsync(new RenderRequest
            {
                Template = new Template
                {
                    Content = "foo",
                    Engine = Engine.None,
                    Recipe = Recipe.Html
                }
            });

            using (var reader = new StreamReader(result.Content))
            {
                reader.ReadToEnd().ShouldBe("foo");
            }
        }           

        [Test]
        public async Task JsRenderTest()
        {
            var result = await _reportingService.RenderAsync(new
            {
                template = new
                {
                    content = "{{:foo}}",
                    engine = "jsrender",
                    recipe = "html"
                },
                data = new
                    {
                        foo = "hello"
                    }
            });

            using (var reader = new StreamReader(result.Content))
            {
                reader.ReadToEnd().ShouldStartWith("hello");
            }
        }
     
        [Test]
        public async Task GetRecipesTest()
        {
            var recipes = await _reportingService.GetRecipesAsync();

            recipes.Count().ShouldBeGreaterThan(1);
        }

        [Test]
        public async Task GetEnginesTest()
        {
            var engines = await _reportingService.GetRecipesAsync();

            engines.Count().ShouldBeGreaterThan(1);            
        }
     
        [Test]
        public async Task ThrowReadableExceptionForInvalidEngineTest()
        {
            try
            {
                var result = await _reportingService.RenderAsync(new {
                    template = new {
                        content = "foo",
                        engine = "NOT_EXISTING",
                        recipe = "chrome-pdf"
                    }
                });
            }
            catch (JsReportException ex)
            {
                ex.ResponseErrorMessage.ShouldContain("NOT_EXISTING");
            }
        }

        [Test]        
        public void HttpClientTimeoutCancelsTaskTest()
        {
            _reportingService.HttpClientTimeout = new TimeSpan(1);

            var aggregate = Should.Throw<AggregateException>(() => _reportingService.RenderAsync(new
            {
                template = new
                {
                    content = "foo",
                    engine = "none",
                    recipe = "chrome-pdf"
                }
            }).Wait());

            aggregate.InnerExceptions.Single().ShouldBeOfType<TaskCanceledException>();
        }

        [Test]
        public void RenderCancelTokenParameterCancelsTaskTest()
        {
            var ts = new CancellationTokenSource();
            ts.CancelAfter(1);

            var aggregate = Should.Throw<AggregateException>(() => _reportingService.RenderAsync(new
            {
                template = new
                {
                    content = "foo",
                    engine = "none",
                    recipe = "chrome-pdf"
                }
            }, ts.Token).Wait());

            aggregate.InnerExceptions.Single().ShouldBeOfType<TaskCanceledException>();
        
        }

        [Test]        
        public async Task SlowRenderingThrowsJsReportExceptionTest()
        {
            await _reportingService.RenderAsync(new
            {
                template = new
                {
                    content = "{{:~foo()}}",
                    helpers = "function foo() { while(true) { } }",
                    engine = "jsrender",
                    recipe = "chrome-pdf"
                }
            }).ShouldThrowAsync<JsReportException>();
        }

        [Test]
        public async Task GetServerVersionTest()
        {
            var result = await _reportingService.GetServerVersionAsync();
            result.ShouldContain(".");
        }

        [Test]
        public async Task RenderInPreviewReturnsExcelOnlineTest()
        {
            var report = await _reportingService.RenderAsync(new
            {

                template = new {
                    content = "<table><tr><td>a</td></tr></table>",
                    recipe = "html-to-xlsx",
                    engine = "jsrender",
                    htmlToXlsx = new { htmlEngine = "chrome" }
                },
                options = new
                {
                    preview = true
                }
            });


            var reader = new StreamReader(report.Content);

            var str = reader.ReadToEnd();
            str.ShouldContain("iframe");
        }     
    }

    
    [TestFixture]
    [SingleThreaded]
    public class AuthenticatedReportingServiceTest
    {
        private IReportingService _reportingService;
        private ILocalWebServerReportingService _localReportingService;

        [SetUp]
        public async Task SetUp()
        {
            Console.WriteLine("Set up");
            _localReportingService = new LocalReporting().KillRunningJsReportProcesses().UseBinary(JsReportBinary.GetBinary()).Configure(cfg => cfg.Authenticated("admin", "password")).AsWebServer().Create();
            await _localReportingService.StartAsync();
            _reportingService = new ReportingService("http://localhost:5488", "admin", "password");
        }

        [TearDown]
        public async Task TearDown()
        {
            Console.WriteLine("Tear down");
            await _localReportingService.KillAsync();
        }

        [Test]
        public async Task CallWithCredentialsWorksTest()
        {
            await _reportingService.GetServerVersionAsync();
        }

        [Test]        
        public async Task CallWithoutCredentialsThrows()
        {
            _reportingService.Username = null;
            _reportingService.Password = null;
            var report = await _reportingService.RenderAsync(new RenderRequest()
            {
                Template = new Template()
                {
                    Content = "foo",
                    Engine = Engine.None,
                    Recipe = Recipe.Html
                }
            }).ShouldThrowAsync<JsReportException>();
        }        
    }   
}
