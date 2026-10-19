1. backend will be in c# dotnet core
2. db will be mysql
3. dedup will be using a persitend expression column continaing a crc32 hash unsigned int of the url with an index so we can eaisly check on duplicates, also the db is source of truth we can interups the process and ocnitnue, also we can store a simple in memory dicitonary<int,url>() or sorted set to quickly validate a duplicate wihtout bothering the db, we can use a lru or rotating buffer in case we are afraid of memeory overflows
4. c# nullability checks are required to fix
5. tree view should be a ready component not a homegrown thing
6. you can first iterate build locally and later move to docker i have plenty docker hosts, i cna open you a window on tmux
7. use rabbitmq
8. a page has a reference to its parent no entity called edge, however this field must be indexed (and should be a second tbale with a many to many relationship, because since we dedup we cna have apge being a child of two pages, so we can eaisly get a listing of all sub pages, state on readme how we can disable this index case
9. make the tree clickable so one can open upa repr of the page
10. implement simhash of every page, and make like a quick google search where one can put in keywords and get a hamming distance
11. foro guid use v1, explain that security (v4) is no need here, and we can benfit from efficient date search
12. status column use enum, and store as int, make a seperate transitions table, with navigaitonal properties, case we want to add a new status
13. if possible use char instead of int8 and use 'p' 'r' 'c', in case we want to use the db from non typesafe languages, however in c# code we must always use the enum
14. api use post, make a simple static session based auth, that is cryptographicly verified (use ed22519 to keep it short) so the two services can validate the session wihtout any db, ellobrate on skipepe dthat we skipped roles for now, can be later added if needed using a proper auth backend
15. read tree result can grow big, so it should download 3 levels at a time and reactively extend the tree
16. have a lowcal searhc in this tree coudl use the simhash
17. the event stream should send found link events, and resolved link events, so the api service can update progress, at any time a found link event is found it is entered in the db with blank text, and a generated bit field that is indexed, so we can quickly count(*) job id indexed vs non indexed, however the outgoing event happens in memory but by anytime we can update our truth by consulting the db
18. since a page can belong to many ongoign jobs, the many to many links tbale will contian the jobid that found that link
19. the many to many table will have duplicates case a page wa slinked by a few jobs
20. the website text will be sotred on disk in a folder structure compared ot the website, however the has will be in the db so we can always cross verify
21. progress hsould look like a 2d graph, is should show in yellow a rising mountain for count, and in blue of bytes, and it should be a bit leighter on the right for the forcast, based on how many links we expect right now, 
22. i'd like to use haxe for the js/react and also use haxe/cs to make a shared project for utilities, and both projects should refer to it for example the encryption
23. we should be using singalr
