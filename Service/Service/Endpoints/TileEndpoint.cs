using Mapster.Common.MemoryMappedTypes;
using Mapster.Rendering;
using SixLabors.ImageSharp;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;


namespace Mapster.Service.Endpoints;

internal class TileEndpoint : IDisposable
{
    private readonly DataFile _mapData;
    private bool _disposedValue;

    public TileEndpoint(string mapDataFilePath)
    {
        _mapData = new DataFile(mapDataFilePath);
    }
    
    
    private static readonly System.Threading.Mutex? ShapesMtx = new (false);

    private static PriorityQueue<BaseShape, int> _shapes = new();

    private static PriorityQueue<BaseShape, int>? shapes
    {
        get
        {
            lock (ShapesMtx)
            {
                return _shapes;
            }
        }
        set
        {
            lock (ShapesMtx)
            {
                _shapes = value;
            }
        }
    }

    private static Image<Rgba32>? _canvas = null;

    private static TileRenderer.BoundingBox _pixelBb;

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
    
    private static PriorityQueue<BaseShape, int> Clone(ref PriorityQueue<BaseShape, int> queue)
    {
        PriorityQueue<BaseShape, int> newOriginal = new();
        PriorityQueue<BaseShape, int> copy = new();
        while (queue.Count > 0)
        {
            queue.TryDequeue(out var shape, out var priority);
            newOriginal.Enqueue(shape, priority);
            copy.Enqueue(shape, priority);
        }
        queue = newOriginal;
        return copy;
    }

    private static void MakeRequestData(double minLat, double minLon, double maxLat, double maxLon, int sizeX, int sizeY, TileEndpoint tileEndpoint)
    {
        if (shapes is { Count: > 0 })
        {
            return;
        }
        _pixelBb = new TileRenderer.BoundingBox
        {
            MinX = float.MaxValue,
            MinY = float.MaxValue,
            MaxX = float.MinValue,
            MaxY = float.MinValue
        };
        var tmpShapes = shapes;
        tileEndpoint._mapData.ForeachFeature(
            new BoundingBox(
                new Coordinate(minLat, minLon),
                new Coordinate(maxLat, maxLon)
            ),
            featureData =>
            {
                featureData.Tessellate(ref _pixelBb, ref tmpShapes);
                return true;
            }
        );
        shapes = tmpShapes;
    }

    public static void Register(WebApplication app)
    {
        // Map HTTP GET requests to this
        // Set up the request as parameterized on 'boundingBox', 'width' and 'height'
        app.MapGet("/render", HandleTileRequest);
        async Task HandleTileRequest(HttpContext context, double minLat, double minLon, double maxLat, double maxLon, int? size, TileEndpoint tileEndpoint)
        {
            size ??= 800;

            context.Response.ContentType = "image/png";
            MakeRequestData(minLat, minLon, maxLat, maxLon, size.Value, size.Value, tileEndpoint);
            var sacrificeShapes = Clone(ref _shapes);
            await tileEndpoint.RenderPng(context.Response.BodyWriter.AsStream(), _pixelBb, sacrificeShapes, size.Value, size.Value);
        }
    }

    private async Task RenderPng(Stream outputStream, TileRenderer.BoundingBox boundingBox, PriorityQueue<BaseShape, int> shapes, int width, int height)
    {
        if(_canvas == null)
        {
            _canvas = await Task.Run(() => { return shapes.Render(boundingBox, width, height); })
                .ConfigureAwait(continueOnCapturedContext: false);
        }

        await _canvas.SaveAsPngAsync(outputStream).ConfigureAwait(continueOnCapturedContext: false);
        
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                _mapData.Dispose();
            }

            _disposedValue = true;
        }
    }
}
