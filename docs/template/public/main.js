/*
    The docfx "modern" template reads this file for options that the site cannot set from
    docfx.json. It supplies an empty one, and this replaces it.

    The icon links go in the navigation bar, at the right of the search box. The names are
    Bootstrap Icons names, which is the icon set the template already loads.
*/
export default {
    iconLinks: [
        {
            icon: 'github',
            href: 'https://github.com/MorseCode-Software/SodaFlow',
            title: 'GitHub'
        },
        {
            icon: 'box-seam',
            href: 'https://www.nuget.org/profiles/jam40jeff',
            title: 'NuGet'
        }
    ]
};
