/*
*  Copyright (C) X Gemeente
*                X Amsterdam
*                X Economic Services Departments
*
*  Licensed under the EUPL, Version 1.2 or later (the "License");
*  You may not use this work except in compliance with the License.
*  You may obtain a copy of the License at:
*
*    https://github.com/Amsterdam/3DAmsterdam/blob/master/LICENSE.txt
*
*  Unless required by applicable law or agreed to in writing, software
*  distributed under the License is distributed on an "AS IS" basis,
*  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or
*  implied. See the License for the specific language governing
*  permissions and limitations under the License.
*/

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using Netherlands3D.Utilities;
using Netherlands3D.Coordinates;
using TMPro;

namespace Netherlands3D.CartesianTiles
{
	[AddComponentMenu("Netherlands3D/CartesianTiles/GeoJSONtextLayer")]
	public class GeoJSONTextLayer : Layer
	{
		public GameObject textPrefab;
		public string geoJsonUrl = "https://geodata.nationaalgeoregister.nl/kadastralekaart/wfs/v4_0?service=WFS&version=2.0.0&request=GetFeature&TypeNames=kadastralekaartv4:openbareruimtenaam&&propertyName=plaatsingspunt,tekst,hoek,relatieveHoogteligging,openbareRuimteType&outputformat=geojson&srs=EPSG:28992&bbox=";//121000,488000,122000,489000";

		[SerializeField]
		protected int maxSpawnsPerFrame = 100;

		public List<TextsAndSize> textsAndSizes = new List<TextsAndSize>();

		public PositionSourceType positionSourceType = PositionSourceType.Point;
		public AutoOrientationMode autoOrientationMode = AutoOrientationMode.AutoFlip;

		private List<string> uniqueNames;

		public bool drawGeometry = false;
		public Material lineRenderMaterial;
		public Color lineColor;
		public float lineWidth = 5.0f;

		[Header("Optional:")]
		public bool readAngleFromProperty = false;
		public string angleProperty = "hoek";
		public bool filterUniqueNames = true;
		public float textMinDrawDistance = 0;

		private void Awake()
		{
			uniqueNames = new List<string>();
		}

		public enum AutoOrientationMode
		{
			None,
			FaceCamera,
			AutoFlip
		}
		public void SetAutoOrientationMode(string autoOrientationMode)
		{
			switch(autoOrientationMode)	{
				case "FaceCamera":
					this.autoOrientationMode = AutoOrientationMode.FaceCamera;
					break;
				case "AutoFlip":
					this.autoOrientationMode = AutoOrientationMode.AutoFlip;
					break;
				default:
					this.autoOrientationMode = AutoOrientationMode.None;
					break;
			}
		}
		public void SetAutoOrientationMode(AutoOrientationMode mode)
		{
			this.autoOrientationMode = mode;
		}
		public void SetPositionSourceType(string positionSourceType)
		{
			switch (positionSourceType)
			{
				case "Point":
					this.positionSourceType = PositionSourceType.Point;
					break;
				case "MultiPolygonCentroid":
					this.positionSourceType = PositionSourceType.MultiPolygonCentroid;
					break;
				default:
					this.positionSourceType = PositionSourceType.Point;
					break;
			}
		}
		public void SetPositionSourceType(PositionSourceType type)
		{
			this.positionSourceType = type;
		}

		public enum PositionSourceType
		{
			Point,
			MultiPolygonCentroid
		}

		[System.Serializable]
		public class TextsAndSize
		{
			public string textPropertyName = "";
			public float drawWithSize = 1.0f;
			public float offset = 10.0f;
		}

		public override void HandleTile(TileChange tileChange, System.Action<TileChange> callback = null)
		{
			TileAction action = tileChange.action;
			var tileKey = new Vector2Int(tileChange.X, tileChange.Y);
			switch (action)
			{
				case TileAction.Create:
					Tile newTile = CreateNewTile(tileKey);
					tiles.Add(tileKey, newTile);
					newTile.runningCoroutine = StartCoroutine(DownloadTextNameData(tileChange, newTile, callback));
					break;
				case TileAction.Upgrade:
					tiles[tileKey].unityLOD++;
					break;
				case TileAction.Downgrade:
					tiles[tileKey].unityLOD--;
					break;
				case TileAction.Remove:
					InteruptRunningProcesses(tileKey);
					RemoveGameObjectFromTile(tileKey);
					tiles.Remove(tileKey);
					callback?.Invoke(tileChange);
					return;
				default:
					break;
			}
		}

		private Tile CreateNewTile(Vector2Int tileKey)
		{
			Tile tile = new Tile();
			tile.unityLOD = 0;
			tile.tileKey = tileKey;
			tile.layer = transform.gameObject.GetComponent<Layer>();
			tile.gameObject = new GameObject();
			tile.gameObject.transform.parent = transform.gameObject.transform;
			tile.gameObject.layer = tile.gameObject.transform.parent.gameObject.layer;
			tile.gameObject.transform.position = CoordinateConverter.RDtoUnity(tileKey);

			return tile;
		}

		protected virtual void RemoveGameObjectFromTile(Vector2Int tileKey)
		{
			if (tiles.ContainsKey(tileKey))
			{
				Tile tile = tiles[tileKey];
				if (tile == null)
				{
					return;
				}
				if (tile.gameObject == null)
				{
					return;
				}
				MeshFilter mf = tile.gameObject.GetComponent<MeshFilter>();
				if (mf != null)
				{
					Destroy(tile.gameObject.GetComponent<MeshFilter>().sharedMesh);
				}
				Destroy(tiles[tileKey].gameObject);

			}
		}

		private GameObject CreateMultiPolygonGeometry(List<double> coordinates)
		{
			var lineRenderObject = new GameObject();
			LineRenderer newLineRenderer = lineRenderObject.AddComponent<LineRenderer>();
			newLineRenderer.positionCount = coordinates.Count / 2;
			newLineRenderer.material = lineRenderMaterial;
			newLineRenderer.startWidth = lineWidth;
			newLineRenderer.endWidth = lineWidth;
			newLineRenderer.startColor = lineColor;
			newLineRenderer.endColor = lineColor;

			for (int i = 0; i < coordinates.Count; i++)
			{
				if (i % 2 == 0)
				{
					var centroidX = coordinates[i];
					var centroidY = coordinates[i + 1];
					var linePoint = CoordinateConverter.RDtoUnity(new Vector2RD(centroidX, centroidY));
					newLineRenderer.SetPosition(Mathf.FloorToInt(i / 2), linePoint);
				}
			}

			return lineRenderObject;
		}

		protected virtual IEnumerator DownloadTextNameData(TileChange tileChange, Tile tile, System.Action<TileChange> callback = null)
		{
			string url = $"{geoJsonUrl}{tileChange.X},{tileChange.Y},{(tileChange.X + tileSize)},{(tileChange.Y + tileSize)}";

			var streetnameRequest = UnityWebRequest.Get(url);
			tile.runningWebRequest = streetnameRequest;
			yield return streetnameRequest.SendWebRequest();

			if (streetnameRequest.result == UnityWebRequest.Result.Success)
			{
				GeoJSON customJsonHandler = new GeoJSON(streetnameRequest.downloadHandler.text);
				yield return null;
				Vector3 locationPoint = default;
				int featureCounter = 0;

				while (customJsonHandler.GotoNextFeature())
				{
					featureCounter++;
					if ((featureCounter % maxSpawnsPerFrame) == 0) yield return null;

					//string textPropertyValue = customJsonHandler.getPropertyStringValue(textProperty);
					foreach(TextsAndSize textAndSize in textsAndSizes)
					{
						string textPropertyValue = customJsonHandler.GetPropertyStringValue(textAndSize.textPropertyName);

						if (textPropertyValue.Length > 1 && (!filterUniqueNames || !uniqueNames.Contains(textPropertyValue)))
						{
							//Instantiate a new text object
							var textObject = Instantiate(textPrefab);
							textObject.name = textPropertyValue;
							textObject.transform.SetParent(tile.gameObject.transform, true);
							textObject.GetComponent<TextMeshPro>().text = textPropertyValue;

							if(filterUniqueNames)
								uniqueNames.Add(textPropertyValue);

							//Determine text position by either a geometry point node, or the centroid of a geometry MultiPolygon node
							switch (positionSourceType)
							{
								case PositionSourceType.Point:
									double[] coordinate = customJsonHandler.GetGeometryPoint2DDouble();
									locationPoint = CoordinateConverter.RDtoUnity(new Vector2RD(coordinate[0], coordinate[1]));
									locationPoint.y = textAndSize.offset;

									//Turn the text object so it faces up
									textObject.transform.Rotate(Vector3.left, -90, Space.Self);

									break;
								case PositionSourceType.MultiPolygonCentroid:
									List<double> coordinates = customJsonHandler.GetGeometryMultiPolygonString();

									if (drawGeometry)
									{
										CreateMultiPolygonGeometry(coordinates).transform.SetParent(textObject.transform,false);
									}

									double minX = double.MaxValue;
									double minY = double.MaxValue;
									double maxX = -double.MaxValue;
									double maxY = -double.MaxValue;
									for (int i = 0; i < coordinates.Count; i++)
									{
										if (i % 2 == 0)
										{
											if (coordinates[i] < minX)	minX = coordinates[i];
											else if (coordinates[i] > maxX)	maxX = coordinates[i];

											if (coordinates[i + 1] < minY) minY = coordinates[i + 1];
											else if (coordinates[i + 1] > maxY) maxY = coordinates[i + 1];
										}
									}

									double centerX = minX + ((maxX - minX) / 2);
									double centerY = minY + ((maxY - minY) / 2);

									locationPoint = CoordinateConverter.RDtoUnity(new Vector2RD(centerX, centerY));
									locationPoint.y = textAndSize.offset;
									break;
							}
							textObject.transform.position = locationPoint;
							textObject.transform.localScale = Vector3.one * textAndSize.drawWithSize;

							//Determine how the spawned texts auto orientate
							switch (autoOrientationMode)
							{
								case AutoOrientationMode.FaceCamera:
									var faceToCameraText = textObject.AddComponent<FaceToCamera>();
									faceToCameraText.HideDistance = textMinDrawDistance;
									faceToCameraText.UniqueNamesList = uniqueNames;
									break;
								case AutoOrientationMode.AutoFlip:
									if (readAngleFromProperty)
									{
										float angle = customJsonHandler.GetPropertyFloatValue(angleProperty);
										textObject.transform.Rotate(Vector3.up, angle, Space.World);
									}
									var flipToCameraText = textObject.AddComponent<FlipToCamera>();
									flipToCameraText.UniqueNamesList = uniqueNames;
									break;
								case AutoOrientationMode.None:
								default:
									break;
							}
						}
					}
				}
				yield return null;
			}
			callback?.Invoke(tileChange);
		}
	}
}
