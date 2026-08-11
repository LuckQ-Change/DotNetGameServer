namespace DotNetGameServer.UnityClient.Protocol
{
    public interface IEnvelopeCodec
    {
        byte[] Encode(NetworkEnvelope envelope);
        NetworkEnvelope Decode(byte[] bytes);
    }
}
