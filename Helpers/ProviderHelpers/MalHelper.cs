namespace jellyfin_ani_sync.Helpers.ProviderHelpers; 

public class MalHelper {
    public static string TruncateQuery(string query) {
        query = StringFormatter.RemoveSpaces(query);
        if (query.Length > 64) {
            query = query.Substring(0, 64);
        }

        return query;
    }
}