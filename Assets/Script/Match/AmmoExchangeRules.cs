public static class AmmoExchangeRules
{
    public static int Count(bool is42, bool remote) => remote ? (is42 ? 10 : 100) : (is42 ? 1 : 10);
    public static int Price(bool is42, bool remote) => remote ? 150 : 10;
    public static int Limit(bool is42) => is42 ? 100 : 1000;
    public const float Delay = 6f;
}
