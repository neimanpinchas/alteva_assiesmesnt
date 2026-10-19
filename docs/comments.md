a. move the dtos to the shared project in a model folder, this shoudl be the source for the javascript models too
b. why do the crawler have direct access to the db, i think it would beteer to avoid this and send anything by the protocol
c. the haxe utils, should also be compiled to and consumed by c#, since you already created the c# i thikn keep it, but make a interface for everything so we can confirm the two sources do not diverge
d. ui box should be lanient with url formats, with/out http auto.com
e. the bytes downloaded on top is not reactive
f. the yellow line should have its own axis has otherwise it is buried
g. clicking on the hamming distance should also display the text
h. the text is too far below, i thikn that a tabbed based interface is better, three tabs crawl status, link tree view, and fast freetext search
also we should have a side bar where we see the list of all files searched
i. in the history list, i want to see basic statistics of link ratio
j. the bytes chart i think doesnt show forcasted finish time

# new comments
a. missing global search
b. we need basic url rewrite so we can relao dht epage
c. option to delete and prune anything related only to this site, be casefull aboutshared links
d. forcast pages should not go on top, the shoudl continue the yellow mountain up to the forcasted and time
e. the simahs/haming sounds too technical it shoudl be only visible on hover, also when clicking ona document it shoudl higlight the words matching
f. Searched Files Sidebar you did not undersand me, i meant a left sidebar with the history list, not only the full page
g. its hard to ctrl+c on the crwler we must have a way to cancel exit it
h. for netfree.link pages crwled shows 83 and the chart shows only 18 i think it doesnt read back the db please double check, also the singal r conneciton is now flowing, i'd expect ot see constant movement of bytes, maybe we should paralalize the crwler, also make sure we make a isolated rety http clientret with max retry, also we should not retry on 400 class, as thi sis a dead link anyway
i. i see in the logs requests being made twice      Start processing HTTP request GET https://www.cal-online.co.il/
info: System.Net.Http.HttpClient.CrawlClient.ClientHandler[100]
 Sending HTTP request GET https://www.cal-online.co.il/
info: System.Net.Http.HttpClient.CrawlClient.ClientHandler[100]
 Sending HTTP request GET https://www.cal-online.co.il/
info: System.Net.Http.HttpClient.CrawlClient.ClientHandler[100]
 Sending HTTP request GET https://www.cal-online.co.il/ we should immediatly block this using url hash and only add a link

 # code review
 why is         uint urlCrc = UrlNormalizer.ComputeCrc32(msg.Url); calculated in code, this shoudl go into a persited virtual mysql field with an index on it
 is auth ever checked? can we use c# filter ufncitnality for that?
        string? normalizedUrl = UrlNormalizer.Normalize(rawUrl); what is the if statement right before why couldnt that go into the normalizer it self

        please use attributes for navigaitonal properties instead of onModelcreating

        is GetJobMetrics recursive or it only gets the top level?
        please profile the api end points to see where we cna get faster
                foreach (var pageId in pageIds) please replace this with a single query not a loop over queries, or do two select count(*) where pageid in ourlist and condition
                            int remainingEstimate = Math.Max(0, 50 - job.TotalPagesCrawled); nonsense we should check how many not yet fetched pages we have and device this by pages per second
                            all page id cols, should not be a stirng they shoudl srictly be int
                            UrlCrc32 should be a key/index
                            put every dto in its own file, make a scirpt to do the migration and update all imports if needed
                            ok with hmac, just do not call it ed25519, my point was to not use rsa, but symetric is ok for now
                                                j.status === 'Completed' ? 'bg-green-900/60 text-green-300' :
do not place colors in markup, they should be part of the client class to caculate the color for the job
do not hardcode configs, keep them in appsettings.json
front end is missing a setting for max page limit
add option to pause resume a crwl anytime
            if (processedUrls.Contains(currentUrl)) is redundant we can just use the dictinary of the uints, i think we should pass the hash in the message to save the worker form ever calculating it, the dto should have a method of eaithe rcalulating it or resuing form cache unless the url is modified
            the html file name should not be the uuid, but it should replicate the folder struture form the website, in case the crwler jumped to another domain  we begin a new root folder
            what is the haxe cretae simple auth token?

# newer comments,
a. in readme and all md files, use mermaid charts instead of ascci art
b. make a file TRICKS.md and mention all tricks such as simhash, url hash index, enum using char, in memory dict to aovid db lookup, and more that i forgot, and link readme.md to it
what will hapen if the db is down or the mq can we make it reprt nicely?
# section 4
please make a good gitignore file, and commit to github
on delete list doesnt update
# section 5
a. once completed it shoudl no longer hsow a forecast
b. link tree view stopped showing tree, siunds like links are broken
# seciton 6
once done the chart should show the history of the crawl evolvement, not just 0 and current, it can also show how much time, and what speed it used
change the file store to an interface and implemnt a mock s3 impl for reference but not to use
add a page explaining the benefits of using haxe ot share common logic and the interfaces used ot enforce contracts, mention that openapi specs, and nwag are more common i chose that path
add a openapi.yaml and open api style interactive docs, maybe use nswag style for dtos, and haxe for shared proceedures
its overrunngin the max pages
- remove job details
- prrove tha tpageing works
- do not use cards, use seprator lines
-
