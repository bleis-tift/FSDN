﻿namespace FSDN

open System.Collections.Generic
open System.Runtime.Serialization
open Microsoft.FSharp.Reflection
open FSharpApiSearch

module private SearchOptionLiteral =

  [<Literal>]
  let Strict = "strict"

  [<Literal>]
  let Similarity = "similarity"

  [<Literal>]
  let IgnoreArgStyle = "ignore_arg_style"

[<DataContract>]
type FSharpApi = {
  [<field: DataMember(Name = "name")>]
  Name: string
  [<field: DataMember(Name = "kind")>]
  Kind: string
  [<field: DataMember(Name = "signature")>]
  Signature: string
}

[<DataContract>]
type SearchResult = {
  [<field: DataMember(Name = "distance")>]
  Distance: int
  [<field: DataMember(Name = "api")>]
  Api: FSharpApi
}

[<DataContract>]
type SearchOptions = {
  [<field: DataMember(Name = SearchOptionLiteral.Strict)>]
  Strict: string
  [<field: DataMember(Name = SearchOptionLiteral.Similarity)>]
  Similarity: string
  [<field: DataMember(Name = SearchOptionLiteral.IgnoreArgStyle)>]
  IgnoreArgStyle: string
}

[<DataContract>]
type SearchInformation = {
  [<field: DataMember(Name = "target_assemblies")>]
  Targets: string []
  [<field: DataMember(Name = "search_options")>]
  RawOptions: SearchOptions
  [<field: DataMember(Name = "query")>]
  Query: string
}

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module FSharpApi =

  let toSerializable (results: FSharpApiSearch.Result seq) =
    {
      Values =
        results
        |> Seq.map (fun result ->
          {
            Distance = result.Distance
            Api =
              {
                Name = ReverseName.toString result.Api.Name
                Kind = result.Api.Kind.Print()
                Signature = result.Api.Signature.Print()
              }
          })
        |> Seq.toArray
    }

  module OptionStatus =

    let tryParse = function
    | "enabled" -> Some Enabled
    | "disabled" -> Some Disabled
    | _ -> None

    let parseOrDefault defaultValue value =
      match tryParse value with
      | Some value -> value
      | None -> defaultValue

  module SearchOptions =

    open Suave
    open SearchOptionLiteral

    let parse info =
      let updateStrict value opt =
        { opt with StrictQueryVariable = OptionStatus.parseOrDefault SearchOptions.defaultOptions.StrictQueryVariable value }
      let updateSimilarity value opt =
        { opt with SimilaritySearching = OptionStatus.parseOrDefault SearchOptions.defaultOptions.SimilaritySearching value }
      let updateIgnoreArgStyle value opt =
        { opt with IgnoreArgumentStyle = OptionStatus.parseOrDefault SearchOptions.defaultOptions.IgnoreArgumentStyle value }
      SearchOptions.defaultOptions
      |> updateStrict info.RawOptions.Strict
      |> updateSimilarity info.RawOptions.Similarity
      |> updateIgnoreArgStyle info.RawOptions.IgnoreArgStyle

    let defaultRawOptions =
      let toString (x: OptionStatus) =
        match FSharpValue.GetUnionFields(x, typeof<OptionStatus>) with
        | case, _ -> case.Name.ToLower()
      {
        Strict = toString SearchOptions.defaultOptions.StrictQueryVariable
        Similarity = toString SearchOptions.defaultOptions.SimilaritySearching
        IgnoreArgStyle = toString SearchOptions.defaultOptions.IgnoreArgumentStyle
      }

  let trySearch database info =
    let client = FSharpApiSearchClient(info.Targets, database)
    try
      client.Search(info.Query, SearchOptions.parse info)
      |> Seq.filter (fun x -> x.Distance < 3)
      |> Choice1Of2
    with e -> Choice2Of2 e
