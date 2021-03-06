namespace myFeed.Tests.Services

open Xunit
open Autofac
open NSubstitute
open System
open System.IO
open System.Linq
open System.Threading.Tasks
open System.Collections.Generic
open myFeed.Tests.Extensions
open myFeed.Tests.Extensions.Domain
open myFeed.Repositories.Abstractions
open myFeed.Repositories.Models
open myFeed.Services.Abstractions
open myFeed.Services.Implementations
open myFeed.Services.Platform
open myFeed.Services.Models

module CachingSettingsServiceFixture =

    [<Fact>]
    let ``should resolve default values from received defaults service``() =    
        
        let defaults = Substitute.For<IDefaultsService>()
        defaults.DefaultSettings.Returns(dict
            [ "Foo", "StoredFoo"; 
              "Bar", "StoredBar" ] |> Dictionary<_, _>) 
            |> ignore

        let service = produce<CachingSettingsService> [defaults]
        service.GetAsync("Foo").Result |> Should.equal "StoredFoo"
        service.GetAsync("Bar").Result |> Should.equal "StoredBar"

    [<Fact>]
    let ``should put new value into cache when set method is called``() =

        let service = produce<CachingSettingsService> []
        service.SetAsync("Zoo", "StoredZoo").Wait()
        service.GetAsync("Zoo").Result |> Should.equal "StoredZoo"
        
    [<Fact>]
    let ``should support generic convertible numbers serialization``() =

        let service = produce<CachingSettingsService> []
        service.SetAsync("Foo", 42).Wait()
        service.GetAsync<int>("Foo").Result |> Should.equal 42    

    [<Fact>]
    let ``should support floating point numbers serialization``() =

        let service = produce<CachingSettingsService> []
        service.SetAsync<float>("Foo", 42.53).Wait()
        service.GetAsync<float>("Foo").Result |> Should.equal 42.53
        
    [<Fact>]
    let ``should support bytes serialization also``() =

        let service = produce<CachingSettingsService> []
        service.SetAsync<byte>("Foo", 1uy).Wait()
        service.GetAsync<byte>("Foo").Result |> Should.equal 1uy

    [<Fact>]
    let ``should throw when trying to extract unknown value``() =    

        let service = produce<CachingSettingsService> []
        fun () -> service.GetAsync("Unknown").Result |> ignore
        |> Should.throw<AggregateException>
        
    [<Fact>]
    let ``should query the database only once when using get method``() =

        let mutable counter = 0
        let settings = Substitute.For<ISettingsRepository>() 
        settings.When(fun x -> x.GetByKeyAsync("Foo") |> ignore)
                .Do(fun _ -> counter <- counter + 1)

        let defaults = Substitute.For<IDefaultsService>()
        defaults.DefaultSettings.Returns(dict["Foo", "Bar"] |> Dictionary<_, _>) |> ignore

        let service = produce<CachingSettingsService> [settings; defaults]
        service.GetAsync("Foo").Result |> Should.equal "Bar"
        service.GetAsync("Foo").Result |> Should.equal "Bar"
        service.GetAsync("Foo").Result |> Should.equal "Bar"
        counter |> Should.equal 1

    [<Fact>]
    let ``should lock repository access from multiple threads and query db only once``() =    

        let mutable counter = 0
        let settings = Substitute.For<ISettingsRepository>() 
        settings.When(fun x -> x.GetByKeyAsync("Foo") |> ignore)
                .Do(fun _ -> counter <- counter + 1)

        let defaults = Substitute.For<IDefaultsService>()
        defaults.DefaultSettings.Returns(dict["Foo", "Bar"] |> Dictionary<_, _>) |> ignore

        let service = produce<CachingSettingsService> [settings; defaults]

        // Access service getter 3 times at once from different threads.
        let thread = fun () -> Task.Run(fun () -> service.GetAsync("Foo").Result)
        Task.WhenAll([thread(); thread(); thread()]) |> ignore

        // Counter should equal 1, not four. Lock should work properly.
        service.GetAsync("Foo").Result |> Should.equal "Bar"
        Should.equal 1 counter

module OpmlServiceFixture =

    [<Fact>]
    let ``should be able to export opml feeds``() =

        let categories = Substitute.For<ICategoriesRepository>()
        categories.GetAllAsync().Returns(
            [ Category(Title="Foo"); 
              Category(Title="Bar", Channels=toList
                [| Channel(Uri="http://example.com/rss") |]) ]
            |> fun seq -> seq.OrderBy(fun i -> i.Title)
            |> Task.FromResult) 
            |> ignore

        let mutable opml = null
        let serializer = Substitute.For<ISerializationService>()
        serializer.When(fun x -> x.Serialize<Opml>(Arg.Any(), Arg.Any()) |> ignore)     
                  .Do(fun x -> opml <- x.Arg<Opml>())

        let service = produce<OpmlService> [categories; serializer]
        let response = service.ExportOpmlFeedsAsync(new MemoryStream()).Result

        Should.equal true response
        Should.equal 2 opml.Body.Count
        Should.equal "Bar" opml.Body.[0].Title
        Should.equal "Foo" opml.Body.[1].Title
        Should.equal 1 opml.Body.[0].ChildOutlines.Count
        Should.equal "example.com" opml.Body.[0].ChildOutlines.[0].Title
        Should.equal "http://example.com" opml.Body.[0].ChildOutlines.[0].HtmlUrl
        Should.equal "http://example.com/rss" opml.Body.[0].ChildOutlines.[0].XmlUrl

    [<Fact>]
    let ``should be able to import opml feeds``() =    

        let serializer = Substitute.For<ISerializationService>()
        serializer.Deserialize<Opml>(Arg.Any()).Returns(
            Opml(Body=toList
                [ OpmlOutline(XmlUrl="http://foo.com");
                  OpmlOutline(XmlUrl="https://bar.com") ])) 
            |> ignore

        let mutable category = null
        let categories = Substitute.For<ICategoriesRepository>()
        categories.When(fun x -> x.InsertAsync(Arg.Any()) |> ignore)
                  .Do(fun x -> category <- x.Arg<Category>())

        let service = produce<OpmlService> [serializer; categories]
        let response = service.ImportOpmlFeedsAsync(new MemoryStream()).Result

        Should.equal true response
        Should.equal 2 category.Channels.Count 
        Should.equal "http://foo.com" category.Channels.[0].Uri
        Should.equal "https://bar.com" category.Channels.[1].Uri

module FeedlySearchServiceFixture =

    [<Fact>]
    let ``should always return valid response``() =

        let service = produce<FeedlySearchService> []
        service.SearchAsync("Foo").Result 
        |> Should.notEqual null

module FeedReaderFetchServiceFixture =

    [<Fact>]
    let ``should always return valid response``() =

        let service = produce<FeedReaderFetchService> []
        service.FetchAsync("nonsense").Result 
        |> also (fst >> Should.notEqual null)   
        |> also (snd >> Should.notEqual null) 
        |> ignore

module RegexImageServiceFixture =

    [<Fact>]
    let ``should extract first image url from plain text``() =

        let service = produce<RegexImageService> []
        "Foo <bla a='42'></bla> \n<img foo='bar' src='http://example.com' />"
        |> (service.ExtractImageUri >> Should.equal "http://example.com")            

    [<Fact>]
    let ``should return null if there are no images``() =
    
        let service = produce<RegexImageService> []
        "London is the capital of Great Britain"
        |> (service.ExtractImageUri >> Should.equal null)

    [<Fact>]
    let ``should return exactly first match from text``() =    

        let service = produce<RegexImageService> []
        "?<img src='http://bar.foo' /> <img src='http://foo.bar' />"
        |> (service.ExtractImageUri >> Should.equal "http://bar.foo")

    [<Fact>]
    let ``should return null if passed string is null``() =

        let service = produce<RegexImageService> []
        null |> (service.ExtractImageUri >> Should.equal null)    
        
module XmlSerializationServiceFixture =

    [<Fact; CleanUpFile("sample")>]
    let ``should serialize typed objects into xml``() =

        let service = produce<XmlSerializationService> []
        let instance = Opml(Head=OpmlHead(Title="Foo"))
        service.Serialize<Opml>(instance, File.OpenWrite "sample")
        Should.contain "Foo" (File.ReadAllText "sample")

    [<Fact; CleanUpFile("sample")>]
    let ``should deserialize typed objects from xml``() =

        let service = produce<XmlSerializationService> []
        let instance = Opml(Head=OpmlHead(Title="Bar"))
        service.Serialize<Opml>(instance, File.OpenWrite "sample")
        let opml = service.Deserialize<Opml>(File.OpenRead "sample")
        Should.equal "Bar" opml.Head.Title

module ParallelFeedStoreServiceFixture =

    [<Fact>]
    let ``should sort stored article entities``() = 

        let settings = Substitute.For<ISettingsService>()
        settings.GetAsync<_>(Arg.Any()).Returns(5) |> ignore

        let fetcher = Substitute.For<IFeedFetchService>()
        fetcher.FetchAsync(Arg.Any<_>()).Returns(
            (null, Seq.empty<Article>) 
            |> Task.FromResult)
            |> ignore

        let service = produce<ParallelFeedStoreService> [fetcher; settings]
        let articles =
            service.LoadAsync(
                [ Channel(Articles=toList
                    [ Article(Title="Foo", PublishedDate=DateTime.Now);
                      Article(Title="Bar", PublishedDate=DateTime.MinValue);
                      Article(Title="Zoo", PublishedDate=DateTime.MaxValue) ]
                  )]).Result
            |> (snd >> List.ofSeq)              

        Should.equal 3 articles.Length
        Should.equal "Zoo" articles.[0].Title
        Should.equal "Foo" articles.[1].Title
        Should.equal "Bar" articles.[2].Title

    [<Fact>]
    let ``should save fetched article entities``() =    

        let settings = Substitute.For<ISettingsService>()
        settings.GetAsync<_>(Arg.Any()).Returns(5) |> ignore

        let fetcher = Substitute.For<IFeedFetchService>()
        fetcher.FetchAsync(Arg.Any<_>()).Returns(
            (null, [Article(Title="Foo")] :> seq<_>) 
            |> Task.FromResult)
            |> ignore

        let mutable articlesInserted = null
        let categories = Substitute.For<ICategoriesRepository>()
        categories.When(fun x -> x.InsertArticleRangeAsync(Arg.Any<_>(), Arg.Any<_>()) |> ignore)
                  .Do(fun x -> articlesInserted <- x.Arg<seq<Article>>())       

        let service = produce<ParallelFeedStoreService> [fetcher; categories; settings]
        let articles = 
            service.LoadAsync(
                [ Channel(Uri="http://foo.bar") ]).Result
                |> (snd >> List.ofSeq)            

        let article = Seq.item 0 articlesInserted
        Should.equal 1 articles.Length
        Should.equal "Foo" articles.[0].Title
        Should.equal "Foo" article.Title

    [<Fact>]
    let ``should mix and order fetched and stored articles by date``() = 
        
        let settings = Substitute.For<ISettingsService>()
        settings.GetAsync<_>(Arg.Any()).Returns(5) |> ignore

        let fetcher = Substitute.For<IFeedFetchService>()
        fetcher.FetchAsync(Arg.Any<_>()).Returns(
            (null, [Article(Title="Foo", PublishedDate=DateTime.Now)] :> seq<_>) 
            |> Task.FromResult)
            |> ignore

        let service = produce<ParallelFeedStoreService> [fetcher; settings]
        let articles =
            service.LoadAsync(
                [ Channel(Articles=toList
                    [ Article(Title="Bar", PublishedDate=
                        DateTime.MinValue)])]).Result
            |> (snd >> List.ofSeq)

        Should.equal 2 articles.Length
        Should.equal "Foo" articles.[0].Title
        Should.equal "Bar" articles.[1].Title         

    [<Fact>]
    let ``should remove outdated articles if count is greater than custom``() = 

        let settings = Substitute.For<ISettingsService>()
        settings.GetAsync<_>(Arg.Any()).Returns(70) |> ignore

        let articles = Seq.init 200 (fun _ -> Article())
        let channel = Channel(Articles=toList articles)
        let fetcher = Substitute.For<IFeedFetchService>()
        fetcher.FetchAsync(Arg.Any()).Returns((null, Seq.empty) 
            |> Task.FromResult) |> ignore
        
        let service = produce<ParallelFeedStoreService> [fetcher; settings]
        let articles = service.LoadAsync([channel]).Result |> (snd >> List.ofSeq)

        Should.equal 70 articles.Length

    [<Fact>]
    let ``should remove articles with minimum publishing date only``() =    
    
        let settings = Substitute.For<ISettingsService>()
        settings.GetAsync<_>(Arg.Any()).Returns(70) |> ignore

        let articles = Seq.init 200 (fun _ -> Article())
        let fetcher = Substitute.For<IFeedFetchService>()
        fetcher.FetchAsync(Arg.Any()).Returns((null, articles) 
            |> Task.FromResult) |> ignore
        
        let service = produce<ParallelFeedStoreService> [fetcher; settings]
        let articles = service.LoadAsync([Channel()]).Result |> (snd >> List.ofSeq)

        Should.equal 70 articles.Length
        
    [<Fact>]
    let ``should ignore whitespaces while comparing titles``() =
        
        let settings = Substitute.For<ISettingsService>()
        settings.GetAsync<_>(Arg.Any()).Returns(70) |> ignore
        
        let fetcher = Substitute.For<IFeedFetchService>()
        fetcher.FetchAsync(Arg.Any<_>()).Returns(
            (null, [ Article(Title="Foo  ", FeedTitle="Bar");
                     Article(Title="Bar\r\n", FeedTitle="Foo")] :> seq<_>) 
            |> Task.FromResult) |> ignore
        
        let service = produce<ParallelFeedStoreService> [fetcher; settings]
        let articles = 
            service.LoadAsync(
                [ Channel(Articles=toList 
                    [ Article(Title="Foo", FeedTitle="Bar");
                      Article(Title="Bar", FeedTitle="Foo") ]) ]).Result 
                    |> (snd >> List.ofSeq)
        
        Should.equal 2 articles.Length
        Should.equal "Foo" articles.[0].Title
        Should.equal "Bar" articles.[1].Title
        Should.equal "Bar" articles.[0].FeedTitle
        Should.equal "Foo" articles.[1].FeedTitle

module FavoritesServiceFixture =

    [<Fact>]
    let ``should update article entity fave value when adding and removing``() =
        
        let article = Article(Fave=false)        
        let service = produce<FavoritesService> []
        
        service.Insert(article).Wait()
        Should.equal article.Fave true

        service.Remove(article).Wait()
        Should.equal article.Fave false

    [<Fact>]
    let ``should insert and remove article via repository``() =
    
        let mutable deleted = 0
        let mutable inserted = 0

        let favorites = Substitute.For<IFavoritesRepository>()
        favorites.When(fun x -> x.InsertAsync(Arg.Any<_>()) |> ignore)
                 .Do(fun _ -> inserted <- inserted + 1)
        favorites.When(fun x -> x.RemoveAsync(Arg.Any<_>()) |> ignore)
                 .Do(fun _ -> deleted <- deleted + 1)             

        let article = Article(Fave=false)
        let service = produce<FavoritesService> [favorites]

        service.Insert(article).Wait()
        service.Insert(article).Wait()
        service.Remove(article).Wait()
        service.Remove(article).Wait()

        Should.equal 1 deleted
        Should.equal 1 inserted

module AutofacFactoryServiceFixture =

    type Sample(name: string) = member __.Name = name

    [<Fact>]
    let ``should inject parameters with given type``() =

        let containerBuilder = ContainerBuilder()
        containerBuilder.RegisterType<Sample>().AsSelf() |> ignore
        let lifetimeScope = containerBuilder.Build()

        let factory = produce<AutofacFactoryService> [lifetimeScope]
        let instance = factory.CreateInstance<Sample> "Foo"
        Should.equal "Foo" instance.Name

module DefaultsServiceFixture =

    [<Fact>]
    let ``should resolve all needed default settings``() =

        let service = produce<DefaultsService> []
        let settings = service.DefaultSettings
        
        settings.["LoadImages"] |> Should.notBeNull
        settings.["NeedBanners"] |> Should.notBeNull
        settings.["NotifyPeriod"] |> Should.notBeNull
        settings.["MaxArticlesPerFeed"] |> Should.notBeNull
        settings.["LastFetched"] |> Should.notBeNull
        settings.["FontSize"] |> Should.notBeNull
        settings.["Theme"] |> Should.notBeNull

module BackgroundServiceFixture = 

    [<Fact>]
    let ``should send ordered notifications for articles with greater date``() =

        let articles = [ Article(Title="Foo", PublishedDate=DateTime.Now);
                         Article(Title="Bar", PublishedDate=DateTime.MaxValue) ]
        let store = Substitute.For<IFeedStoreService>()
        store.LoadAsync(Arg.Any()).Returns(
            (null, articles.OrderBy(fun x -> x.Id)) 
            |> Task.FromResult) 
            |> ignore

        let settings = Substitute.For<ISettingsService>()
        settings.GetAsync(Arg.Any()).Returns(
            DateTime.MinValue |> Task.FromResult)
            |> ignore      

        let mutable received = null
        let notify = Substitute.For<INotificationService>()
        notify.When(fun x -> x.SendNotifications(Arg.Any()) |> ignore)
              .Do(fun x -> received <- x.Arg<List<Article>>())

        let service = produce<BackgroundService> [store; settings; notify]
        service.CheckForUpdates(DateTime.Now).Wait()

        Should.equal "Foo" received.[0].Title
        Should.equal "Bar" received.[1].Title

    [<Fact>]
    let ``should not send notifications for outdated old articles``() =

        let articles = [ Article(Title="Foo", PublishedDate=DateTime.MinValue) ]
        let store = Substitute.For<IFeedStoreService>()
        store.LoadAsync(Arg.Any()).Returns(
            (null, articles.OrderBy(fun x -> x.Id)) 
            |> Task.FromResult) 
            |> ignore

        let mutable received = null
        let notify = Substitute.For<INotificationService>()
        notify.When(fun x -> x.SendNotifications(Arg.Any()) |> ignore)
              .Do(fun x -> received <- x.Arg<List<Article>>())

        let service = produce<BackgroundService> [store; notify]
        service.CheckForUpdates(DateTime.Now).Wait()

        Should.equal 0 received.Count 
