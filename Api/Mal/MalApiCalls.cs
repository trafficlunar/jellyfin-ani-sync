using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using jellyfin_ani_sync.Configuration;
using jellyfin_ani_sync.Helpers;
using jellyfin_ani_sync.Models;
using jellyfin_ani_sync.Models.Mal;
using MediaBrowser.Controller;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace jellyfin_ani_sync.Api {
    public class MalApiCalls : AuthApiCall {
        private readonly ILogger<MalApiCalls> _logger;

        private string ApiUrl => $"https://api.myanimelist.net/v2";

        public MalApiCalls(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory, IServerApplicationHost serverApplicationHost, IHttpContextAccessor httpContextAccessor, UserConfig userConfig) :
            base(ApiName.Mal, httpClientFactory, serverApplicationHost, httpContextAccessor, loggerFactory, userConfig) {
            _logger = loggerFactory.CreateLogger<MalApiCalls>();
        }

        /// <summary>
        /// Get a users information.
        /// </summary>
        public async Task<User> GetUserInformation() {
            UrlBuilder url = new UrlBuilder {
                Base = $"{ApiUrl}/users/@me"
            };
            var apiCall = await AuthenticatedApiCall(ApiName.Mal, CallType.GET, url.Build());
            if (apiCall != null) {
                StreamReader streamReader = new StreamReader(await apiCall.Content.ReadAsStreamAsync());
                string streamText = await streamReader.ReadToEndAsync();

                return JsonSerializer.Deserialize<User>(streamText);
            } else {
                return null;
            }
        }

        /// <summary>
        /// Search the MAL database for anime.
        /// </summary>
        /// <param name="query">Search by title.</param>
        /// <param name="fields">The fields you would like returned.</param>
        /// <param name="updateNsfw">True to return NSFW anime.</param>
        /// <returns>List of anime.</returns>
        public async Task<List<Anime>> SearchAnime(string query, string[] fields, bool updateNsfw = false) {
            UrlBuilder url = new UrlBuilder {
                Base = $"{ApiUrl}/anime"
            };
            if (query != null) {
                // must truncate query to 64 characters. MAL API returns an error otherwise
                query = StringFormatter.RemoveSpaces(query);
                if (query.Length > 64) {
                    query = query.Substring(0, 64);
                }

                url.Parameters.Add(new KeyValuePair<string, string>("q", query));
                if (updateNsfw) {
                    url.Parameters.Add(new KeyValuePair<string, string>("nsfw", "true"));
                }
            }

            if (fields != null) {
                url.Parameters.Add(new KeyValuePair<string, string>("fields", String.Join(",", fields)));
            }

            string builtUrl = url.Build();
            _logger.LogInformation($"(MAL) Starting search for anime (GET {builtUrl})...");
            var apiCall = await AuthenticatedApiCall(ApiName.Mal, CallType.GET, builtUrl);
            if (apiCall != null) {
                StreamReader streamReader = new StreamReader(await apiCall.Content.ReadAsStreamAsync());
                var animeList = JsonSerializer.Deserialize<SearchAnimeResponse>(await streamReader.ReadToEndAsync());

                _logger.LogInformation("(MAL) Search complete");
                return animeList.Data.Select(list => list.Anime).ToList();
            }

            return null;
        }

        /// <summary>
        /// Get an anime from the MAL database.
        /// </summary>
        /// <returns></returns>
        public async Task<Anime> GetAnime(int animeId, string[]? fields = null) {
            UrlBuilder url = new UrlBuilder {
                Base = $"{ApiUrl}/anime/{animeId}"
            };

            if (fields != null) {
                url.Parameters.Add(new KeyValuePair<string, string>("fields", String.Join(",", fields)));
            }

            string builtUrl = url.Build();
            _logger.LogInformation($"(MAL) Retrieving an anime from MAL (GET {builtUrl})...");
            try {
                var apiCall = await AuthenticatedApiCall(ApiName.Mal, CallType.GET, builtUrl);
                if (apiCall != null) {
                    StreamReader streamReader = new StreamReader(await apiCall.Content.ReadAsStreamAsync());
                    var options = new JsonSerializerOptions();
                    options.Converters.Add(new JsonStringEnumConverter());
                    var anime = JsonSerializer.Deserialize<Anime>(await streamReader.ReadToEndAsync(), options);
                    _logger.LogInformation("(MAL) Anime retrieval complete");
                    return anime;
                } else {
                    return null;
                }
            } catch (Exception e) {
                _logger.LogError(e.Message);
                return null;
            }
        }

        public enum Sort {
            ListScore,
            ListUpdatedAt,
            AnimeTitle,
            AnimeStartDate,
            AnimeId
        }

        public async Task<List<UserAnimeListData>> GetUserAnimeList(Status? status = null, Sort? sort = null, int? idSearch = null) {
            UrlBuilder url = new UrlBuilder {
                Base = $"{ApiUrl}/users/@me/animelist"
            };

            url.Parameters.Add(new KeyValuePair<string, string>("fields", "list_status,num_episodes"));

            if (status != null) {
                url.Parameters.Add(new KeyValuePair<string, string>("status", status.Value.ToString().ToLower()));
            }

            if (sort != null) {
                url.Parameters.Add(new KeyValuePair<string, string>("sort", StringFormatter.ConvertEnumToString('_', sort).ToLower()));
            }

            string builtUrl = url.Build();
            UserAnimeList userAnimeList = new UserAnimeList { Data = new List<UserAnimeListData>() };
            while (true) {
                _logger.LogInformation($"(MAL) Getting user anime list (GET {builtUrl})...");
                var apiCall = await AuthenticatedApiCall(ApiName.Mal, CallType.GET, builtUrl);
                if (apiCall != null) {
                    StreamReader streamReader = new StreamReader(await apiCall.Content.ReadAsStreamAsync());
                    var options = new JsonSerializerOptions {
                        Converters = { new JsonStringEnumConverter() }
                    };
                    UserAnimeList userAnimeListPage = JsonSerializer.Deserialize<UserAnimeList>(await streamReader.ReadToEndAsync(), options);

                    if (userAnimeListPage?.Data is { Count: > 0 }) {
                        if (idSearch != null) {
                            var foundAnime = userAnimeListPage.Data.FirstOrDefault(anime => anime.Anime.Id == idSearch);
                            if (foundAnime != null) {
                                return new List<UserAnimeListData> { foundAnime };
                            }
                        } else {
                            userAnimeList.Data = userAnimeList.Data.Concat(userAnimeListPage.Data).ToList();
                        }

                        if (userAnimeListPage.Paging.Next != null) {
                            builtUrl = userAnimeListPage.Paging.Next;
                            _logger.LogInformation("(MAL) Additional pages found; waiting 2 seconds before calling again...");
                            Thread.Sleep(2000);
                        } else {
                            break;
                        }
                    } else {
                        break;
                    }
                } else {
                    break;
                }
            }

            _logger.LogInformation("(MAL) Got user anime list");
            return userAnimeList.Data.ToList();
        }

        public async Task<UpdateAnimeStatusResponse> UpdateAnimeStatus(int animeId, int numberOfWatchedEpisodes, Status? status = null,
            bool? isRewatching = null, int? numberOfTimesRewatched = null, DateTime? startDate = null, DateTime? endDate = null) {
            UrlBuilder url = new UrlBuilder {
                Base = $"{ApiUrl}/anime/{animeId}/my_list_status"
            };

            List<KeyValuePair<string, string>> body = new List<KeyValuePair<string, string>> {
                new("num_watched_episodes", numberOfWatchedEpisodes.ToString())
            };

            if (status != null) {
                body.Add(new KeyValuePair<string, string>("status", status.Value.ToString().ToLower()));
            }

            if (isRewatching != null && isRewatching.Value) {
                body.Add(new KeyValuePair<string, string>("is_rewatching", true.ToString()));
            } else {
                body.Add(new KeyValuePair<string, string>("is_rewatching", false.ToString().ToLower()));
            }

            if (numberOfTimesRewatched != null) {
                body.Add(new KeyValuePair<string, string>("num_times_rewatched", numberOfTimesRewatched.Value.ToString()));
            }

            if (startDate != null) {
                body.Add(new KeyValuePair<string, string>("start_date", startDate.Value.ToString("yyyy-MM-dd")));
            }

            if (endDate != null) {
                body.Add(new KeyValuePair<string, string>("finish_date", endDate.Value.ToString("yyyy-MM-dd")));
            }

            var builtUrl = url.Build();

            UpdateAnimeStatusResponse updateResponse;
            try {
                var apiCall = await AuthenticatedApiCall(ApiName.Mal, CallType.PUT, builtUrl, new FormUrlEncodedContent(body.ToArray()));

                if (apiCall != null) {
                    StreamReader streamReader = new StreamReader(await apiCall.Content.ReadAsStreamAsync());
                    var options = new JsonSerializerOptions();
                    options.Converters.Add(new JsonStringEnumConverter());
                    _logger.LogInformation($"(MAL) Updating anime status (PUT {builtUrl})...");
                    updateResponse = JsonSerializer.Deserialize<UpdateAnimeStatusResponse>(await streamReader.ReadToEndAsync(), options);
                    _logger.LogInformation("(MAL) Update complete");
                } else {
                    updateResponse = null;
                }
            } catch (Exception e) {
                _logger.LogError($"(MAL) Error updating anime status: {e.Message}");
                updateResponse = null;
            }

            return updateResponse;
        }
    }
}
