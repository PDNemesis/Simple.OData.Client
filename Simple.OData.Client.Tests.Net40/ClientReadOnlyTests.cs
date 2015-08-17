﻿using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace Simple.OData.Client.Tests
{
    using Entry = System.Collections.Generic.Dictionary<string, object>;

    public class ClientReadOnlyTests : TestBase
    {
        public ClientReadOnlyTests()
            : base(true)
        {
        }

        [Fact]
        public async Task FindEntries()
        {
            var products = await _client.FindEntriesAsync("Products");
            Assert.True(products.Any());
        }

        [Fact]
        public async Task FindEntriesNonExisting()
        {
            var products = await _client.FindEntriesAsync("Products?$filter=ProductID eq -1");
            Assert.True(!products.Any());
        }

        [Fact]
        public async Task FindEntriesSelect()
        {
            var products = await _client.FindEntriesAsync("Products?$select=ProductName");
            Assert.Equal(1, products.First().Count);
            Assert.Equal("ProductName", products.First().First().Key);
        }

        [Fact]
        public async Task FindEntriesFilterAny()
        {
            var orders = await _client.FindEntriesAsync("Orders?$filter=Order_Details/any(d:d/Quantity gt 50)");
            Assert.Equal(160, orders.Count());
        }

        [Fact]
        public async Task FindEntriesFilterAll()
        {
            var orders = await _client.FindEntriesAsync("Orders?$filter=Order_Details/all(d:d/Quantity gt 50)");
            Assert.Equal(11, orders.Count());
        }

        [Fact]
        public async Task FindEntry()
        {
            var product = await _client.FindEntryAsync("Products?$filter=ProductName eq 'Chai'");
            Assert.Equal("Chai", product["ProductName"]);
        }

        [Fact]
        public async Task FindEntryNuGetV1()
        {
            var client = new ODataClient("http://nuget.org/api/v1");
            var package = await client.FindEntryAsync("Packages?$filter=Title eq 'EntityFramework'");
            Assert.NotNull(package["Id"]);
            Assert.NotNull(package["Authors"]);
        }

        [Fact]
        public async Task FindEntryNuGetV2()
        {
            var client = new ODataClient("http://nuget.org/api/v2");
            var package = await client.FindEntryAsync("Packages?$filter=Title eq 'EntityFramework'");
            Assert.NotNull(package["Id"]);
        }

        [Fact]
        public async Task FindEntryNuGetV2_FieldWithAnnotation()
        {
            var client = new ODataClient("http://nuget.org/api/v2");
            var package = await client.FindEntryAsync("Packages?$filter=Title eq 'EntityFramework'");
            Assert.NotNull(package["Authors"]);
        }

        [Fact]
        public async Task GetEntryExisting()
        {
            var product = await _client.GetEntryAsync("Products", new Entry() { { "ProductID", 1 } });
            Assert.Equal("Chai", product["ProductName"]);
        }

        [Fact]
        public async Task GetEntryExistingCompoundKey()
        {
            var orderDetail = await _client.GetEntryAsync("OrderDetails", new Entry() { { "OrderID", 10248 }, { "ProductID", 11 } });
            Assert.Equal(11, orderDetail["ProductID"]);
        }

        [Fact]
        public async Task GetEntryNonExisting()
        {
            await AssertThrowsAsync<WebRequestException>(async () => await _client.GetEntryAsync("Products", new Entry() { { "ProductID", -1 } }));
        }

        [Fact]
        public async Task GetEntryNonExistingIgnoreException()
        {
            var settings = new ODataClientSettings
            {
                BaseUri = _serviceUri,
                IgnoreResourceNotFoundException = true,
            };
            var client = new ODataClient(settings);
            var product = await client.GetEntryAsync("Products", new Entry() { { "ProductID", -1 } });

            Assert.Null(product);
        }

        [Fact]
        public async Task ExecuteScalarFunctionWithStringParameter()
        {
            var result = await _client.ExecuteFunctionAsScalarAsync<int>("ParseInt", new Entry() { { "number", "1" } });
            Assert.Equal(1, result);
        }

        [Fact]
        public async Task ExecuteScalarFunctionWithLongParameter()
        {
            var result = await _client.ExecuteFunctionAsScalarAsync<long>("PassThroughLong", new Entry() { { "number", 1L } });
            Assert.Equal(1L, result);
        }

        [Fact]
        public async Task ExecuteScalarFunctionWithDateTimeParameter()
        {
            var dateTime = new DateTime(2013, 1, 1, 12, 13, 14, 789, DateTimeKind.Utc);
            var result = await _client.ExecuteFunctionAsScalarAsync<DateTime>("PassThroughDateTime", new Entry() { { "dateTime", dateTime } });
            Assert.Equal(dateTime.ToUniversalTime(), result);
        }

        [Fact]
        public async Task ExecuteScalarFunctionWithGuidParameter()
        {
            var guid = Guid.NewGuid();
            var result = await _client.ExecuteFunctionAsScalarAsync<Guid>("PassThroughGuid", new Entry() { { "guid", guid } });
            Assert.Equal(guid, result);
        }

        [Fact]
        public async Task InterceptRequest()
        {
            var settings = new ODataClientSettings
            {
                BaseUri = _serviceUri,
                BeforeRequest = x => x.Method = new HttpMethod("PUT"),
            };
            var client = new ODataClient(settings);
            await AssertThrowsAsync<WebRequestException>(async () => await client.FindEntriesAsync("Products"));
        }

        [Fact]
        public async Task InterceptResponse()
        {
            var settings = new ODataClientSettings
            {
                BaseUri = _serviceUri,
                AfterResponse = x => { throw new InvalidOperationException(); },
            };
            var client = new ODataClient(settings);
            await AssertThrowsAsync<InvalidOperationException>(async () => await client.FindEntriesAsync("Products"));
        }

        [Fact]
        public async Task FindEntryExistingDynamicFilter()
        {
            var x = ODataDynamic.Expression;
            string filter = await (Task<string>)_client.GetCommandTextAsync("Products", x.ProductName == "Chai");
            var product = await _client.FindEntryAsync(filter);
            Assert.Equal("Chai", product["ProductName"]);
        }

        [Fact]
        public async Task FindBaseClassEntryDynamicFilter()
        {
            var x = ODataDynamic.Expression;
            string filter = await (Task<string>)_client.GetCommandTextAsync("Transport", x.TransportID == 1);
            var ship = await _client.FindEntryAsync(filter);
            Assert.Equal("Titanic", ship["ShipName"]);
        }

        [Fact]
        public async Task FindDerivedClassEntryDynamicFilter()
        {
            var x = ODataDynamic.Expression;
            string filter = await (Task<string>)_client.GetCommandTextAsync("Transport/Ships", x.ShipName == "Titanic");
            var ship = await _client.FindEntryAsync(filter);
            Assert.Equal("Titanic", ship["ShipName"]);
        }

        [Fact]
        public async Task FindEntryExistingTypedFilter()
        {
            string filter = await _client.GetCommandTextAsync<Product>("Products", x => x.ProductName == "Chai");
            var product = await _client.FindEntryAsync(filter);
            Assert.Equal("Chai", product["ProductName"]);
        }

        [Fact]
        public async Task FindBaseClassEntryTypedFilter()
        {
            string filter = await _client.GetCommandTextAsync<Transport>("Transport", x => x.TransportID == 1);
            var ship = await _client.FindEntryAsync(filter);
            Assert.Equal("Titanic", ship["ShipName"]);
        }

        [Fact]
        public async Task FindDerivedClassEntryTypedFilter()
        {
            string filter = await _client.GetCommandTextAsync<Ship>("Transport/Ships", x => x.ShipName == "Titanic");
            var ship = await _client.FindEntryAsync(filter);
            Assert.Equal("Titanic", ship["ShipName"]);
        }
    }
}